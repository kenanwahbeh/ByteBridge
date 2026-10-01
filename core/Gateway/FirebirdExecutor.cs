using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FirebirdSql.Data.FirebirdClient;
using ByteBridge.Configuration;

namespace ByteBridge.Gateway;

internal static class FirebirdExecutor
{
    public static string BuildConnectionString(
        DatabaseConfig config)
    {
        var builder =
            new FbConnectionStringBuilder
            {
                DataSource = config.Server,
                Port = config.Port,
                Database = config.Database,
                UserID = config.Username,
                Password = config.Password,
                Charset = "UTF8",
                ConnectionTimeout = 10
            };

        return builder.ToString();
    }

    public static async Task<QueryResponse> QueryAsync(
        DatabaseConfig config,
        string sql,
        IReadOnlyDictionary<string, JsonElement>? parameters,
        int maxRows,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        var response = new QueryResponse();

        await using var connection =
            new FbConnection(
                BuildConnectionString(config));

        await connection.OpenAsync(cancellationToken);

        /*
         * Every /query runs in a transaction that Firebird itself holds
         * read-only, so a statement the text check lets through -- a
         * SELECT from a procedure that writes, say -- is refused by the
         * engine ("attempted update during read-only transaction")
         * instead of being trusted not to write.
         *
         * Read committed with record versions is the usual setting for
         * a read-only transaction: it sees what is committed as each
         * statement starts, and does not hold back the server's clean-up
         * of old row versions while a long result is being read.
         */
        await using var transaction =
            await connection.BeginTransactionAsync(
                new FbTransactionOptions
                {
                    TransactionBehavior =
                        FbTransactionBehavior.Read |
                        FbTransactionBehavior.ReadCommitted |
                        FbTransactionBehavior.RecVersion |
                        FbTransactionBehavior.NoWait
                },
                cancellationToken);

        await using var command =
            connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = sql;
        command.CommandTimeout = commandTimeoutSeconds;

        AddParameters(command, parameters);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

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

            var row =
                new List<object?>(reader.FieldCount);

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

        response.ElapsedMs =
            stopwatch.ElapsedMilliseconds;

        return response;
    }

    public static async Task<ExecuteResponse> ExecuteAsync(
        DatabaseConfig config,
        string sql,
        IReadOnlyDictionary<string, JsonElement>? parameters,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        await using var connection =
            new FbConnection(
                BuildConnectionString(config));

        await connection.OpenAsync(cancellationToken);

        await using var command =
            connection.CreateCommand();

        command.CommandText = sql;
        command.CommandTimeout = commandTimeoutSeconds;

        AddParameters(command, parameters);

        var rowsAffected =
            await command.ExecuteNonQueryAsync(
                cancellationToken);

        stopwatch.Stop();

        return new ExecuteResponse
        {
            RowsAffected = rowsAffected,

            ElapsedMs =
                stopwatch.ElapsedMilliseconds
        };
    }

    /*
     * A quick, plain refusal for a write sent to /query.
     *
     * It is not what keeps /query from writing: the read-only
     * transaction in QueryAsync is. So it only has to be right about
     * the ordinary case, and a statement that gets past it still
     * cannot change anything.
     */
    public static bool IsReadOnlyStatement(string sql)
    {
        var statement = StripLeadingNoise(sql);

        return StartsWithKeyword(statement, "SELECT") ||
               StartsWithKeyword(statement, "WITH");
    }

    private static readonly Regex NextValueFor =
        new(@"\bNEXT\s+VALUE\s+FOR\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex AnyGenId =
        new(@"\bGEN_ID\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // GEN_ID(name, 0): reads the current value and changes nothing.
    private static readonly Regex GenIdPeek =
        new(@"\bGEN_ID\s*\(\s*(?:""[^""]*""|[A-Za-z_][\w$]*)\s*,\s*0\s*\)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /*
     * Whether the statement would move a generator (sequence).
     *
     * A generator changes outside any transaction, so the read-only
     * transaction in QueryAsync does not stop it: GEN_ID(name, 1) or
     * NEXT VALUE FOR inside a SELECT advances it for good, and a
     * rollback does not give the number back. Only a step of exactly 0
     * is allowed through; a step that is a parameter or an expression
     * cannot be proved to be 0, so it is refused too.
     *
     * Quoted text and comments are blanked first, so a string that
     * merely mentions GEN_ID does not trip it.
     *
     * This sees only the statement. A procedure or function that moves
     * a generator inside its own body is out of its sight; stopping
     * that is a matter for the Firebird user's privileges.
     */
    public static bool AdvancesSequence(string sql)
    {
        var text = BlankLiteralsAndComments(sql);

        if (NextValueFor.IsMatch(text))
        {
            return true;
        }

        return AnyGenId.Matches(text).Count != GenIdPeek.Matches(text).Count;
    }

    private static string BlankLiteralsAndComments(string sql)
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

            if (c == '\'')
            {
                i++;

                while (i < sql.Length)
                {
                    if (sql[i] == '\'')
                    {
                        if (i + 1 < sql.Length && sql[i + 1] == '\'')
                        {
                            i += 2;
                            continue;
                        }

                        break;
                    }

                    i++;
                }

                i++;
                result.Append(' ');
                continue;
            }

            result.Append(c);
            i++;
        }

        return result.ToString();
    }

    /*
     * Skips whitespace and leading SQL comments, both line and
     * block form, so a statement that opens with a comment is
     * still recognised by its first real keyword.
     */
    private static string StripLeadingNoise(string sql)
    {
        var index = 0;

        while (index < sql.Length)
        {
            if (char.IsWhiteSpace(sql[index]))
            {
                index++;
                continue;
            }

            if (sql[index] == '-' &&
                index + 1 < sql.Length &&
                sql[index + 1] == '-')
            {
                while (index < sql.Length &&
                       sql[index] != '\n')
                {
                    index++;
                }

                continue;
            }

            if (sql[index] == '/' &&
                index + 1 < sql.Length &&
                sql[index + 1] == '*')
            {
                var end =
                    sql.IndexOf(
                        "*/",
                        index + 2,
                        StringComparison.Ordinal);

                if (end < 0)
                {
                    return string.Empty;
                }

                index = end + 2;
                continue;
            }

            break;
        }

        return sql[index..];
    }

    private static bool StartsWithKeyword(
        string text,
        string keyword)
    {
        if (!text.StartsWith(
                keyword,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (text.Length == keyword.Length)
        {
            return true;
        }

        var next = text[keyword.Length];

        return !char.IsLetterOrDigit(next) &&
               next != '_' &&
               next != '$';
    }

    private static void AddParameters(
        FbCommand command,
        IReadOnlyDictionary<string, JsonElement>? parameters)
    {
        if (parameters == null)
        {
            return;
        }

        foreach (var parameter in parameters)
        {
            var name =
                parameter.Key.StartsWith('@')
                    ? parameter.Key
                    : "@" + parameter.Key;

            command.Parameters.AddWithValue(
                name,
                ToParameterValue(parameter.Value));
        }
    }

    private static object ToParameterValue(
        JsonElement element)
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
                return element.GetString()
                    ?? (object)DBNull.Value;

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

            byte[] bytes =>
                Convert.ToBase64String(bytes),

            string or bool or decimal or double or float or
            byte or sbyte or short or ushort or int or uint or
            long or ulong or DateTime or DateTimeOffset or
            TimeSpan or Guid => value,

            _ => value.ToString()
        };
    }
}
