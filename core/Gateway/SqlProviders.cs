using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FirebirdSql.Data.FirebirdClient;
using Microsoft.Data.SqlClient;
using Npgsql;
using ByteBridge.Configuration;

namespace ByteBridge.Gateway;

/*
 * One database engine, behind the few things the gateway needs from it:
 * open a connection, run a query that cannot write, run a statement that
 * can, and say whether a failure was the database's own.
 *
 * Everything that is the same for every engine lives here once: the row
 * cap, the JSON-friendly values, the parameter binding. What differs is
 * small, and is where the engines differ in what protects a read-only
 * request, so it is spelled out in each engine's class.
 */
internal abstract class SqlProvider
{
    protected abstract DbConnection CreateConnection(DatabaseConfig config);

    /*
     * The transaction every /query runs in. It is never committed, so
     * whatever a statement that fooled the text check managed to change
     * is rolled back when it is disposed. Engines that can also hold the
     * transaction read-only do so, so the engine refuses the write.
     */
    protected abstract Task<DbTransaction> BeginQueryTransactionAsync(
        DbConnection connection,
        CancellationToken cancellationToken);

    /* Whether this exception is the database talking, not a bug here. */
    public abstract bool IsDatabaseError(Exception error);

    /*
     * Engine-specific refusal of a statement that already looks like a
     * SELECT, or null when it may run. The text check is a courtesy;
     * what it adds depends on how much the engine itself guards.
     */
    public virtual string? RejectQuery(string sql) => null;

    public async Task<(bool Succeeded, string? Error)> TestAsync(
        DatabaseConfig config,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = CreateConnection(config);

            await connection.OpenAsync(cancellationToken);
            await connection.CloseAsync();

            return (true, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            /*
             * Being told to stop is not a failed test: reporting it as
             * one would mark a working database offline every time the
             * service shuts down mid-probe.
             */
            throw;
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public async Task<QueryResponse> QueryAsync(
        DatabaseConfig config,
        string sql,
        IReadOnlyDictionary<string, JsonElement>? parameters,
        int maxRows,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        var response = new QueryResponse();

        await using var connection = CreateConnection(config);

        await connection.OpenAsync(cancellationToken);

        await using var transaction =
            await BeginQueryTransactionAsync(connection, cancellationToken);

        await using var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = sql;
        command.CommandTimeout = commandTimeoutSeconds;

        AddParameters(command, parameters);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        for (var i = 0; i < reader.FieldCount; i++)
        {
            response.Columns.Add(reader.GetName(i));
        }

        while (await reader.ReadAsync(cancellationToken))
        {
            /*
             * Stop at the cap rather than materialising a whole
             * table into memory and pushing it through the
             * tunnel. Truncated tells the caller to paginate.
             */
            if (response.Rows.Count >= maxRows)
            {
                response.Truncated = true;
                break;
            }

            var row = new List<object?>(reader.FieldCount);

            for (var i = 0; i < reader.FieldCount; i++)
            {
                row.Add(
                    reader.IsDBNull(i)
                        ? null
                        : NormalizeValue(reader.GetValue(i)));
            }

            response.Rows.Add(row);
        }

        response.RowCount = response.Rows.Count;

        stopwatch.Stop();

        response.ElapsedMs = stopwatch.ElapsedMilliseconds;

        return response;
    }

    public async Task<ExecuteResponse> ExecuteAsync(
        DatabaseConfig config,
        string sql,
        IReadOnlyDictionary<string, JsonElement>? parameters,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        await using var connection = CreateConnection(config);

        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();

        command.CommandText = sql;
        command.CommandTimeout = commandTimeoutSeconds;

        AddParameters(command, parameters);

        var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);

        stopwatch.Stop();

        return new ExecuteResponse
        {
            RowsAffected = rowsAffected,
            ElapsedMs = stopwatch.ElapsedMilliseconds
        };
    }

    private static void AddParameters(
        DbCommand command,
        IReadOnlyDictionary<string, JsonElement>? parameters)
    {
        if (parameters == null)
        {
            return;
        }

        foreach (var parameter in parameters)
        {
            var bound = command.CreateParameter();

            bound.ParameterName =
                parameter.Key.StartsWith('@')
                    ? parameter.Key
                    : "@" + parameter.Key;

            bound.Value = ToParameterValue(parameter.Value);

            command.Parameters.Add(bound);
        }
    }

    private static object ToParameterValue(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                return DBNull.Value;

            case JsonValueKind.True:
                return true;

            case JsonValueKind.False:
                return false;

            case JsonValueKind.String:
                return element.GetString() ?? (object)DBNull.Value;

            case JsonValueKind.Number:
                if (element.TryGetInt64(out var integer))
                {
                    return integer;
                }

                if (element.TryGetDecimal(out var value))
                {
                    return value;
                }

                return element.GetDouble();

            default:
                return element.GetRawText();
        }
    }

    /*
     * Keeps the JSON payload predictable.
     *
     * Blobs come back base64 encoded, and any provider specific
     * type the serializer would not handle is rendered as its
     * string form instead of failing the whole request.
     */
    private static object? NormalizeValue(object? value)
    {
        return value switch
        {
            null => null,

            DBNull => null,

            byte[] bytes => Convert.ToBase64String(bytes),

            string or bool or decimal or double or float or
            byte or sbyte or short or ushort or int or uint or
            long or ulong or DateTime or DateTimeOffset or
            DateOnly or TimeOnly or TimeSpan or Guid => value,

            _ => value.ToString()
        };
    }
}

internal sealed class FirebirdProvider : SqlProvider
{
    public static readonly FirebirdProvider Instance = new();

    public static string BuildConnectionString(DatabaseConfig config) =>
        new FbConnectionStringBuilder
        {
            DataSource = config.Server,
            Port = config.Port,
            Database = config.Database,
            UserID = config.Username,
            Password = config.Password,
            Charset = "UTF8",
            ConnectionTimeout = 10
        }.ToString();

    protected override DbConnection CreateConnection(DatabaseConfig config) =>
        new FbConnection(BuildConnectionString(config));

    /*
     * Held read-only by Firebird itself, so a statement the text check
     * lets through -- a SELECT from a procedure that writes, say -- is
     * refused by the engine ("attempted update during read-only
     * transaction") instead of being trusted not to write.
     *
     * Read committed with record versions is the usual setting for a
     * read-only transaction: it sees what is committed as each statement
     * starts, and does not hold back the server's clean-up of old row
     * versions while a long result is being read.
     */
    protected override async Task<DbTransaction> BeginQueryTransactionAsync(
        DbConnection connection,
        CancellationToken cancellationToken) =>
        await ((FbConnection)connection).BeginTransactionAsync(
            new FbTransactionOptions
            {
                TransactionBehavior =
                    FbTransactionBehavior.Read |
                    FbTransactionBehavior.ReadCommitted |
                    FbTransactionBehavior.RecVersion |
                    FbTransactionBehavior.NoWait
            },
            cancellationToken);

    public override bool IsDatabaseError(Exception error) => error is FbException;
}

internal sealed class PostgreSqlProvider : SqlProvider
{
    public static readonly PostgreSqlProvider Instance = new();

    public static string BuildConnectionString(DatabaseConfig config) =>
        new NpgsqlConnectionStringBuilder
        {
            Host = config.Server,
            Port = config.Port,
            Database = config.Database,
            Username = config.Username,
            Password = config.Password,
            Timeout = 10,
            ApplicationName = "ByteBridge"
        }.ToString();

    protected override DbConnection CreateConnection(DatabaseConfig config) =>
        new NpgsqlConnection(BuildConnectionString(config));

    /*
     * PostgreSQL holds the transaction read-only itself: anything that
     * would change data or schema is refused with "cannot execute ... in
     * a read-only transaction". Sequences are the one thing it still
     * lets move, which is what the sequence check in the text guard is for.
     */
    protected override async Task<DbTransaction> BeginQueryTransactionAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);

        await using var readOnly = connection.CreateCommand();

        readOnly.Transaction = transaction;
        readOnly.CommandText = "SET TRANSACTION READ ONLY";

        await readOnly.ExecuteNonQueryAsync(cancellationToken);

        return transaction;
    }

    public override bool IsDatabaseError(Exception error) => error is NpgsqlException;
}

internal sealed class SqlServerProvider : SqlProvider
{
    public static readonly SqlServerProvider Instance = new();

    /*
     * Encrypted, but the server's certificate is not checked: a SQL
     * Server on a LAN usually has the self-signed one it made for itself,
     * and requiring a trusted one would make the common case fail to
     * connect. The traffic is still not readable on the wire.
     */
    public static string BuildConnectionString(DatabaseConfig config) =>
        new SqlConnectionStringBuilder
        {
            DataSource = $"{config.Server},{config.Port}",
            InitialCatalog = config.Database,
            UserID = config.Username,
            Password = config.Password,
            ConnectTimeout = 10,
            Encrypt = SqlConnectionEncryptOption.Mandatory,
            TrustServerCertificate = true,
            ApplicationName = "ByteBridge"
        }.ToString();

    protected override DbConnection CreateConnection(DatabaseConfig config) =>
        new SqlConnection(BuildConnectionString(config));

    /*
     * SQL Server has no read-only transaction, so the engine does not
     * refuse a write on its own. Two things stand in for it: RejectQuery
     * below, and this transaction, which is never committed, so a write
     * that got past the text check is rolled back when it is disposed.
     * For a database that matters, give the gateway a login that can only
     * read (the db_datareader role); that is the real guarantee.
     */
    protected override async Task<DbTransaction> BeginQueryTransactionAsync(
        DbConnection connection,
        CancellationToken cancellationToken) =>
        await connection.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);

    public override bool IsDatabaseError(Exception error) => error is SqlException;

    private static readonly Regex Forbidden =
        new(@"\b(INSERT|UPDATE|DELETE|MERGE|INTO|EXEC|EXECUTE|DROP|ALTER|CREATE|TRUNCATE|"
            + @"GRANT|REVOKE|DENY|BACKUP|RESTORE|SHUTDOWN|WAITFOR|BULK|"
            + @"OPENROWSET|OPENQUERY|OPENDATASOURCE|OPENXML)\b|\bxp_\w+",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /*
     * WITH can lead into an UPDATE or DELETE here, SELECT can create a
     * table with INTO, and a second statement can follow a semicolon, so
     * "starts with SELECT or WITH" is not enough on this engine. Quoted
     * text, comments and quoted names are blanked first, so a string or a
     * column that merely contains one of these words does not trip it.
     */
    public override string? RejectQuery(string sql)
    {
        var text = SqlText.BlankLiteralsAndComments(sql, blankBracketNames: true).Trim();

        text = text.TrimEnd(';', ' ', '\t', '\r', '\n');

        if (text.Contains(';'))
        {
            return "/query takes one statement. Remove the semicolon and what follows it.";
        }

        var match = Forbidden.Match(text);

        return match.Success
            ? $"/query does not accept {match.Value.ToUpperInvariant()} on SQL Server. "
              + "Use a plain SELECT."
            : null;
    }
}

internal static class SqlProviders
{
    public static SqlProvider For(DatabaseType type) => type switch
    {
        DatabaseType.PostgreSql => PostgreSqlProvider.Instance,
        DatabaseType.SqlServer => SqlServerProvider.Instance,
        _ => FirebirdProvider.Instance
    };

    public static SqlProvider For(DatabaseConfig config) => For(config.Type);
}

/*
 * Text helpers shared by the engines' guards.
 */
internal static class SqlText
{
    /*
     * Replaces string literals, comments and, when asked, [bracketed] and
     * "quoted" names with a space, so a word inside them is not mistaken
     * for a keyword.
     */
    public static string BlankLiteralsAndComments(string sql, bool blankBracketNames = false)
    {
        var result = new StringBuilder(sql.Length);
        var i = 0;

        while (i < sql.Length)
        {
            var c = sql[i];

            if (c == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
            {
                while (i < sql.Length && sql[i] != '\n')
                {
                    i++;
                }

                result.Append(' ');
                continue;
            }

            if (c == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
            {
                var end = sql.IndexOf("*/", i + 2, StringComparison.Ordinal);

                i = end < 0 ? sql.Length : end + 2;
                result.Append(' ');
                continue;
            }

            if (c == '\'' || (blankBracketNames && c == '"'))
            {
                i = SkipQuoted(sql, i, c);
                result.Append(' ');
                continue;
            }

            if (blankBracketNames && c == '[')
            {
                i = SkipQuoted(sql, i, ']');
                result.Append(' ');
                continue;
            }

            result.Append(c);
            i++;
        }

        return result.ToString();
    }

    /*
     * From the opening quote at start, to just past its close. A doubled
     * closing character is an escaped one, as in 'it''s' and [a]]b].
     */
    private static int SkipQuoted(string sql, int start, char close)
    {
        var i = start + 1;

        while (i < sql.Length)
        {
            if (sql[i] == close)
            {
                if (i + 1 < sql.Length && sql[i + 1] == close)
                {
                    i += 2;
                    continue;
                }

                return i + 1;
            }

            i++;
        }

        return i;
    }
}
