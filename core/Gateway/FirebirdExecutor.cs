using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ByteBridge.Configuration;

namespace ByteBridge.Gateway;

internal static class FirebirdExecutor
{
    public static string BuildConnectionString(DatabaseConfig config) =>
        FirebirdProvider.BuildConnectionString(config);

    public static Task<QueryResponse> QueryAsync(
        DatabaseConfig config,
        string sql,
        IReadOnlyDictionary<string, JsonElement>? parameters,
        int maxRows,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken) =>
        FirebirdProvider.Instance.QueryAsync(
            config, sql, parameters, maxRows, commandTimeoutSeconds, cancellationToken);

    public static Task<ExecuteResponse> ExecuteAsync(
        DatabaseConfig config,
        string sql,
        IReadOnlyDictionary<string, JsonElement>? parameters,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken) =>
        FirebirdProvider.Instance.ExecuteAsync(
            config, sql, parameters, commandTimeoutSeconds, cancellationToken);

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

    // PostgreSQL: nextval('seq') moves a sequence, setval('seq', n) sets it.
    private static readonly Regex PostgresSequence =
        new(@"\b(?:nextval|setval)\s*\(",
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

        if (NextValueFor.IsMatch(text) || PostgresSequence.IsMatch(text))
        {
            return true;
        }

        return AnyGenId.Matches(text).Count != GenIdPeek.Matches(text).Count;
    }

    private static string BlankLiteralsAndComments(string sql) =>
        SqlText.BlankLiteralsAndComments(sql);

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
}
