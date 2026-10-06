using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FirebirdSql.Data.FirebirdClient;
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
            ApplicationName = "ByteBridge",

            /*
             * Pooling off, deliberately.
             *
             * A pooled connection is reset when it is next handed out,
             * not when it is returned -- so anything session-scoped a
             * statement left behind outlives the request. A session-level
             * advisory lock is the case that matters: it belongs to the
             * session, so rolling back the transaction does not release
             * it, and the lock would sit held on a connection other
             * clients are waiting for. Without pooling the session ends
             * at the end of the request and the server drops it.
             *
             * The cost is a connection per request, which is nothing
             * beside the tunnel round trip every one of these requests
             * already pays.
             */
            Pooling = false
        }.ToString();

    protected override DbConnection CreateConnection(DatabaseConfig config) =>
        new NpgsqlConnection(BuildConnectionString(config));

    /*
     * LEFT: SQL Server was in this file and has been taken back out.
     *
     * Not for want of the code: SqlServerProvider was written and its
     * guard unit-tested. Two reasons it is not here yet.
     *
     * The guard is not a guarantee here, and that is the point. SQL
     * Server has no read-only transaction, so refusing a write comes
     * down to a text check, and a text check cannot see every shape a
     * SELECT can take -- a UNION reads a table the statement never
     * named, for instance. On Firebird and PostgreSQL the engine
     * itself refuses, so the text check is only ever a courtesy. Here
     * it would be the whole of it.
     *
     * And none of it has ever run against a SQL Server: the image
     * wants more memory than a hosted CI runner offers, so its
     * connection string and guard are covered by unit tests alone.
     * Shipping an engine as the one thing standing between a public
     * tunnel and somebody's database, on tests that never reached a
     * server, is not a trade worth making for a wider feature list.
     *
     * What it would take: a fix for the UNION case, and one real
     * server to run against. SqlServerIntegrationTests is written and
     * passes nothing until BYTEBRIDGE_TEST_SQLSERVER names one.
     */

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

    /*
     * One statement only, and this is the engine's guard rather than a
     * shared one, because here the read-only transaction is not enough
     * on its own.
     *
     * Npgsql sends "SELECT 1; DELETE FROM t" as one batch, so both
     * statements run inside the read-only transaction -- which is
     * why that case is already refused by the engine. But a
     * transaction-control statement in the middle ends it: in
     * "SELECT 1; COMMIT; DELETE FROM t" the COMMIT closes the
     * read-only transaction, and the DELETE that follows runs in a new
     * one that is not read-only. The text check is the only thing
     * standing in that path.
     *
     * So the check is on statement separators, after blanking what can
     * hold one: string literals, comments, quoted names, and
     * dollar-quoted bodies -- a function body written in one is a
     * SELECT that legitimately contains semicolons of its own.
     *
     * A single trailing semicolon is allowed; the alternative is
     * refusing a query a client sent with the semicolon a driver adds.
     *
     * Exactly one, not TrimEnd(';') -- that strips every trailing
     * semicolon, so "SELECT 1;;" was let through while the comment
     * promised otherwise. The extras are empty statements and cannot
     * write, but a rule that reads as "one" and is "one or more" is
     * the kind of gap that gets quoted back later.
     */
    public override string? RejectQuery(string sql)
    {
        var text = SqlText.BlankLiteralsAndComments(
            sql,
            blankBracketNames: true,
            dollarQuoted: true).Trim();

        text = text.TrimEnd(' ', '\t', '\r', '\n');

        if (text.EndsWith(';'))
        {
            text = text[..^1].TrimEnd(' ', '\t', '\r', '\n');
        }

        if (text.Contains(';'))
        {
            return "/query takes one statement on PostgreSQL. Remove " +
                "the semicolon and whatever follows it.";
        }

        /*
         * Session-level advisory locks, refused for a quieter reason
         * than the statement count: a lock taken by pg_advisory_lock
         * belongs to the session rather than the transaction, so
         * rolling this transaction back does not release it, and the
         * lock stays held against whatever else wants it.
         *
         * The transaction-scoped ones (pg_advisory_xact_lock) are left
         * alone: those die with the transaction below.
         *
         * This cannot see a lock taken inside a function the statement
         * calls. Pooling is off for that reason as well as for the
         * direct case: see BuildConnectionString.
         */
        var lockCall = SessionAdvisoryLock.Match(text);

        return lockCall.Success
            ? $"/query does not accept {lockCall.Value.ToUpperInvariant()} " +
              "on PostgreSQL. It would hold a lock after the request " +
              "ends, which other clients then wait on."
            : null;
    }

    /*
     * Every session-level variant, including the shared and exclusive
     * ones and the try_ forms -- all of them belong to the session and
     * none is released by the rollback below. The _xact_lock family is
     * deliberately not matched: those die with the transaction.
     */
    private static readonly Regex SessionAdvisoryLock =
        new(@"\bpg_(?:try_)?advisory_(?:un)?lock"
            + @"(?:_(?:shared|exclusive))?(?:_all)?\s*\(",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}

internal static class SqlProviders
{
    public static SqlProvider For(DatabaseType type) => type switch
    {
        DatabaseType.PostgreSql => PostgreSqlProvider.Instance,
        _ => FirebirdProvider.Instance
    };

    /*
     * Throws for a row whose engine this build cannot serve, rather
     * than falling back to Firebird and reporting a connection failure
     * that is really a configuration one. Callers turn this into a
     * clean answer for the caller -- see UnsupportedEngineException.
     */
    public static SqlProvider For(DatabaseConfig config)
    {
        if (!config.EngineIsSupported)
        {
            throw new UnsupportedEngineException(config.Name);
        }

        return For(config.Type);
    }
}

/*
 * The engine named in a connection row is not one this build has a
 * provider for. Separate from a database error on purpose: nothing is
 * wrong with the database, and saying so is what tells an administrator
 * to go and pick an engine rather than go and fix the server.
 */
public sealed class UnsupportedEngineException : Exception
{
    public UnsupportedEngineException(string connectionName)
        : base($"Connection \"{connectionName}\" names a database engine " +
               "this version of ByteBridge cannot serve. Open it in the " +
               "control panel and choose a supported engine.")
    {
        ConnectionName = connectionName;
    }

    public string ConnectionName { get; }
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
     *
     * dollarQuoted covers PostgreSQL's $tag$...$tag$ strings. A
     * function body is written inside one, and it may hold a semicolon,
     * a quote and a whole second statement -- so a check for statement
     * separators has to skip them or it will read one.
     */
    public static string BlankLiteralsAndComments(
        string sql,
        bool blankBracketNames = false,
        bool dollarQuoted = false)
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

            if (dollarQuoted && c == '$' &&
                TryReadDollarTag(sql, i, out var afterOpener))
            {
                i = SkipDollarQuoted(sql, i, afterOpener, result);
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

    /*
     * A dollar-quote opener: a $, then a tag of letters, digits and
     * underscores, then the closing $. The tag may be empty, which is
     * what "$$...$$" is.
     *
     * A $ that does not open one is left alone: it is far likelier to
     * be a placeholder or an operator than a string.
     */
    private static bool TryReadDollarTag(
        string sql,
        int start,
        out int afterOpener)
    {
        var i = start + 1;

        while (i < sql.Length &&
               (char.IsLetterOrDigit(sql[i]) || sql[i] == '_'))
        {
            i++;
        }

        if (i < sql.Length && sql[i] == '$')
        {
            afterOpener = i + 1;

            return true;
        }

        afterOpener = 0;

        return false;
    }

    /*
     * From the opener through to just past its closing. An unterminated
     * one runs to the end, which is how the server reads it too.
     */
    private static int SkipDollarQuoted(
        string sql,
        int openerStart,
        int afterOpener,
        StringBuilder result)
    {
        var closing = sql[openerStart..afterOpener];

        var end = sql.IndexOf(
            closing,
            afterOpener,
            StringComparison.Ordinal);

        // One space for the whole literal, so nothing inside it can be
        // read as a keyword or a statement separator.
        result.Append(' ');

        return end < 0 ? sql.Length : end + closing.Length;
    }
}
