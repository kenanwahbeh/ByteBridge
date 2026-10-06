using System.Net;
using System.Text.Json;
using Npgsql;
using Xunit;
using ByteBridge.Configuration;
using ByteBridge.Data;
using ByteBridge.Gateway;

namespace ByteBridge.Tests;

/*
 * End to end against a real PostgreSQL server: HTTP in, rows out, and
 * the read-only guarantees checked against the engine itself.
 *
 * Skipped unless BYTEBRIDGE_TEST_POSTGRES=host:port:user:password:database
 * names one, so a clone with no PostgreSQL still gets a green run.
 */
[Trait("Category", "PostgreSql")]
public class PostgreSqlIntegrationTests
{
    private const string Table = "bb_test_customers";
    private const string Sequence = "bb_test_seq";

    private static readonly SemaphoreSlim SchemaGate = new(1, 1);
    private static bool _schemaReady;

    private static async Task RunAsync(string sql) =>
        await PostgreSqlProvider.Instance.ExecuteAsync(
            PostgresTestServer.Connection!, sql, null, 30, CancellationToken.None);

    private static async Task EnsureSchemaAsync()
    {
        await SchemaGate.WaitAsync();

        try
        {
            if (_schemaReady)
            {
                return;
            }

            await RunAsync($"DROP TABLE IF EXISTS {Table}");
            await RunAsync($"DROP SEQUENCE IF EXISTS {Sequence}");

            await RunAsync($"""
                CREATE TABLE {Table} (
                  id integer PRIMARY KEY,
                  name text,
                  balance numeric(12,2),
                  created timestamp,
                  active boolean,
                  notes text
                )
                """);

            await RunAsync($"CREATE SEQUENCE {Sequence} START 100");

            await RunAsync($"""
                INSERT INTO {Table} VALUES
                (1, 'كنان وهبة', 1500.75, '2024-01-15 10:30:00', true, 'ملاحظة عربية'),
                (2, 'Acme Ltd', -42.10, '2024-02-01 08:00:00', false, NULL),
                (3, NULL, NULL, NULL, NULL, NULL)
                """);

            _schemaReady = true;
        }
        finally
        {
            SchemaGate.Release();
        }
    }

    private static async Task<GatewayHarness> LiveGatewayAsync(bool allowWrites = false)
    {
        await EnsureSchemaAsync();

        var gateway = new GatewayHarness();

        gateway.Database.AddConnection(PostgresTestServer.Connection!);
        gateway.Database.SetAllowWrites(allowWrites);

        return gateway;
    }

    private static JsonElement Rows(string body) =>
        JsonDocument.Parse(body).RootElement.GetProperty("rows");

    private static async Task<long> CountAsync(string where = "TRUE")
    {
        var result = await PostgreSqlProvider.Instance.QueryAsync(
            PostgresTestServer.Connection!,
            $"SELECT COUNT(*) FROM {Table} WHERE {where}",
            null, 10, 30, CancellationToken.None);

        return Convert.ToInt64(result.Rows[0][0]);
    }

    [PostgresFact]
    public async Task A_select_returns_columns_rows_and_nulls()
    {
        using var gateway = await LiveGatewayAsync();

        var (status, body) = await GatewayHarness.Read(gateway.Post("/query", new
        {
            database = "Test",
            sql = $"SELECT id, name, balance, active, notes FROM {Table} ORDER BY id"
        }));

        Assert.Equal(HttpStatusCode.OK, status);

        var rows = Rows(body);

        Assert.Equal(3, rows.GetArrayLength());
        Assert.Equal("كنان وهبة", rows[0][1].GetString());
        Assert.Equal(1500.75m, rows[0][2].GetDecimal());
        Assert.True(rows[0][3].GetBoolean());
        Assert.Equal("ملاحظة عربية", rows[0][4].GetString());
        Assert.Equal(JsonValueKind.Null, rows[2][1].ValueKind);
    }

    [PostgresFact]
    public async Task Parameters_are_bound_not_pasted_into_the_statement()
    {
        using var gateway = await LiveGatewayAsync();

        var (status, body) = await GatewayHarness.Read(gateway.Post("/query", new
        {
            database = "Test",
            sql = $"SELECT name FROM {Table} WHERE id = @id",
            parameters = new Dictionary<string, object> { ["id"] = 2 }
        }));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("Acme Ltd", Rows(body)[0][0].GetString());

        // A value that looks like SQL stays a value.
        var (_, hostile) = await GatewayHarness.Read(gateway.Post("/query", new
        {
            database = "Test",
            sql = $"SELECT COUNT(*) FROM {Table} WHERE name = @name",
            parameters = new Dictionary<string, object> { ["name"] = "x'; DROP TABLE " + Table + "; --" }
        }));

        Assert.Equal(0, Rows(hostile)[0][0].GetInt32());
        Assert.Equal(3, await CountAsync());
    }

    [PostgresFact]
    public async Task The_row_cap_truncates_and_says_so()
    {
        using var gateway = await LiveGatewayAsync();

        var (_, body) = await GatewayHarness.Read(gateway.Post("/query", new
        {
            database = "Test",
            sql = "SELECT g FROM generate_series(1, 500) AS g",
            maxRows = 10
        }));

        var document = JsonDocument.Parse(body).RootElement;

        Assert.Equal(10, document.GetProperty("rowCount").GetInt32());
        Assert.True(document.GetProperty("truncated").GetBoolean());
    }

    [PostgresFact]
    public async Task The_engine_itself_refuses_a_write_sent_as_a_query()
    {
        await EnsureSchemaAsync();

        // Straight to the provider, skipping the text check: this is how
        // a statement that fooled the check would arrive.
        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            PostgreSqlProvider.Instance.QueryAsync(
                PostgresTestServer.Connection!,
                $"INSERT INTO {Table} (id, name) VALUES (98765, 'sneaked in')",
                null, 100, 30, CancellationToken.None));

        Assert.Contains("read-only", error.Message, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(0, await CountAsync("id = 98765"));
    }

    /*
     * Both statements run inside the read-only transaction, and the
     * engine refuses the second one. Straight to the provider, with no
     * text guard in the way, which is what proves the engine is doing
     * the refusing -- and it is the reason the text guard above exists:
     * that protection is one COMMIT away from not applying.
     */
    [PostgresFact]
    public async Task The_engine_refuses_a_second_statement_inside_the_read_only_transaction()
    {
        await EnsureSchemaAsync();

        await Assert.ThrowsAsync<PostgresException>(() =>
            PostgreSqlProvider.Instance.QueryAsync(
                PostgresTestServer.Connection!,
                $"SELECT 1; DELETE FROM {Table}",
                null, 100, 30, CancellationToken.None));

        Assert.Equal(3, await CountAsync());
    }

    [PostgresFact]
    public async Task A_second_statement_after_a_select_cannot_write()
    {
        using var gateway = await LiveGatewayAsync();

        /*
         * Refused by ByteBridge rather than by the engine. Npgsql sends
         * "SELECT 1; DELETE FROM t" as one batch, so both statements run
         * inside the read-only transaction and the engine refuses the
         * second one -- which is what the test above proves directly.
         * This one is about the text guard, because a batch is the only
         * way to get a second statement here at all.
         */
        var (status, body) = await GatewayHarness.Read(gateway.Post("/query", new
        {
            database = "Test",
            sql = $"SELECT 1; DELETE FROM {Table}"
        }));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("one statement", body);

        Assert.Equal(3, await CountAsync());
    }

    /*
     * The read-only transaction is not enough on its own here, and this
     * is the statement that proves it.
     *
     * A COMMIT in the middle ends the read-only transaction, and the
     * DELETE after it runs in a new one that is not read-only. So this
     * has to be refused before it reaches the engine: nothing else stops
     * it.
     */
    [PostgresFact]
    public async Task A_commit_in_the_middle_cannot_end_the_read_only_transaction()
    {
        using var gateway = await LiveGatewayAsync();

        var (status, body) = await GatewayHarness.Read(gateway.Post("/query", new
        {
            database = "Test",
            sql = $"SELECT 1; COMMIT; DELETE FROM {Table}"
        }));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("one statement", body);

        Assert.Equal(3, await CountAsync());
    }

    [PostgresFact]
    public async Task An_insert_after_a_commit_cannot_land_either()
    {
        using var gateway = await LiveGatewayAsync();

        var (status, _) = await GatewayHarness.Read(gateway.Post("/query", new
        {
            database = "Test",
            sql = $"SELECT 1; COMMIT; INSERT INTO {Table} (id) VALUES (98767)"
        }));

        Assert.Equal(HttpStatusCode.BadRequest, status);

        Assert.Equal(0, await CountAsync("id = 98767"));
    }

    /*
     * A function body is written inside a dollar-quoted string and
     * legitimately holds semicolons of its own, so the guard has to skip
     * it rather than read one statement where there is only one.
     *
     * The refusal here is the engine's, not the guard's: the guard let
     * it through, which is the point. A DO block is one statement, and
     * PostgreSQL will not run one inside a read-only transaction.
     */
    [PostgresFact]
    public async Task A_dollar_quoted_body_is_not_read_as_a_second_statement()
    {
        using var gateway = await LiveGatewayAsync();

        var (status, body) = await GatewayHarness.Read(gateway.Post("/query", new
        {
            database = "Test",
            sql = """
                DO $$ BEGIN RAISE NOTICE 'a semicolon ; inside a body'; END $$;
                """
        }));

        Assert.Equal(HttpStatusCode.BadRequest, status);

        // Not the guard's wording: it did not count those semicolons as
        // statements.
        Assert.DoesNotContain("one statement", body);

        Assert.Equal(3, await CountAsync());
    }

    /*
     * Session-level advisory locks are the other thing this guard has to
     * refuse, and the reason is quieter than the COMMIT case: a lock
     * taken by pg_advisory_lock belongs to the session, not the
     * transaction, so rolling the transaction back does not release it.
     * A read request could leave a lock held that other clients wait on.
     */
    [PostgresFact]
    public async Task A_session_advisory_lock_cannot_be_taken_from_a_query()
    {
        using var gateway = await LiveGatewayAsync();

        var (status, body) = await GatewayHarness.Read(gateway.Post("/query", new
        {
            database = "Test",
            sql = "SELECT pg_advisory_lock(4242)"
        }));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("advisory", body, StringComparison.OrdinalIgnoreCase);

        /*
         * Deliberately not counting pg_locks here. The claim being made
         * is that the statement never ran, which the refusal above shows;
         * whether a session-level lock can survive is a property of the
         * connection, and the connection is not pooled, so it cannot.
         * A cluster-wide count would only add a test that fails when
         * something unrelated holds a lock.
         */
    }

    [PostgresFact]
    public async Task A_sequence_cannot_be_moved_from_query()
    {
        using var gateway = await LiveGatewayAsync();

        var before = await SequenceValueAsync();

        var (status, body) = await GatewayHarness.Read(gateway.Post("/query", new
        {
            database = "Test",
            sql = $"SELECT nextval('{Sequence}')"
        }));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("generator", body);

        Assert.Equal(before, await SequenceValueAsync());
    }

    private static async Task<long> SequenceValueAsync()
    {
        var result = await PostgreSqlProvider.Instance.QueryAsync(
            PostgresTestServer.Connection!,
            $"SELECT last_value FROM {Sequence}",
            null, 10, 30, CancellationToken.None);

        return Convert.ToInt64(result.Rows[0][0]);
    }

    [PostgresFact]
    public async Task Execute_is_refused_while_the_gateway_is_read_only()
    {
        using var gateway = await LiveGatewayAsync(allowWrites: false);

        var (status, _) = await GatewayHarness.Read(gateway.Post("/execute", new
        {
            database = "Test",
            sql = $"INSERT INTO {Table} (id, name) VALUES (777, 'no')"
        }));

        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.Equal(0, await CountAsync("id = 777"));
    }

    [PostgresFact]
    public async Task Execute_writes_when_writing_is_on_and_reports_the_rows()
    {
        using var gateway = await LiveGatewayAsync(allowWrites: true);

        var (status, body) = await GatewayHarness.Read(gateway.Post("/execute", new
        {
            database = "Test",
            sql = $"INSERT INTO {Table} (id, name) VALUES (@id, @name)",
            parameters = new Dictionary<string, object> { ["id"] = 555, ["name"] = "written" }
        }));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(1, JsonDocument.Parse(body).RootElement.GetProperty("rowsAffected").GetInt32());
        Assert.Equal(1, await CountAsync("id = 555"));

        await RunAsync($"DELETE FROM {Table} WHERE id = 555");
    }

    [PostgresFact]
    public async Task A_sql_error_comes_back_as_a_400_with_the_engines_words()
    {
        using var gateway = await LiveGatewayAsync();

        var (status, body) = await GatewayHarness.Read(gateway.Post("/query", new
        {
            database = "Test",
            sql = "SELECT * FROM table_that_is_not_there"
        }));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("table_that_is_not_there", body);
    }

    [PostgresFact]
    public async Task The_connection_test_tells_good_details_from_bad()
    {
        var good = await DatabaseConnectionTester.TestAsync(PostgresTestServer.Connection!);

        Assert.True(good.Succeeded, good.Error);

        var wrong = new DatabaseConfig
        {
            Type = DatabaseType.PostgreSql,
            Server = PostgresTestServer.Connection!.Server,
            Port = PostgresTestServer.Connection.Port,
            Username = PostgresTestServer.Connection.Username,
            Password = "definitely-not-the-password",
            Database = PostgresTestServer.Connection.Database
        };

        var bad = await DatabaseConnectionTester.TestAsync(wrong);

        Assert.False(bad.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(bad.Error));
    }
}

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (PostgresTestServer.Connection == null)
        {
            Skip =
                "Set BYTEBRIDGE_TEST_POSTGRES=host:port:user:password:database " +
                "to run the live PostgreSQL tests.";
        }
    }
}

internal static class PostgresTestServer
{
    public const string Variable = "BYTEBRIDGE_TEST_POSTGRES";

    public static DatabaseConfig? Connection { get; } = Parse();

    private static DatabaseConfig? Parse()
    {
        var spec = Environment.GetEnvironmentVariable(Variable);

        if (string.IsNullOrWhiteSpace(spec))
        {
            return null;
        }

        var parts = spec.Split(':', 5);

        if (parts.Length != 5 || !int.TryParse(parts[1], out var port))
        {
            throw new InvalidOperationException(
                $"{Variable} must look like host:port:user:password:database, got '{spec}'.");
        }

        return new DatabaseConfig
        {
            Name = "Test",
            Type = DatabaseType.PostgreSql,
            Server = parts[0],
            Port = port,
            Username = parts[2],
            Password = parts[3],
            Database = parts[4]
        };
    }
}
