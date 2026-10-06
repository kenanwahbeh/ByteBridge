using Microsoft.Data.Sqlite;
using Xunit;
using ByteBridge.Configuration;
using ByteBridge.Gateway;

namespace ByteBridge.Tests;

/*
 * The engine a connection talks to: what is stored, what is refused,
 * and that a settings file from before there were other engines keeps
 * working.
 */
public class DatabaseTypeTests
{
    [Theory]
    [InlineData("firebird", DatabaseType.Firebird)]
    [InlineData("PostgreSQL", DatabaseType.PostgreSql)]
    [InlineData("postgres", DatabaseType.PostgreSql)]
    [InlineData("pg", DatabaseType.PostgreSql)]
    public void Engine_names_are_understood(string text, DatabaseType expected)
    {
        Assert.True(DatabaseTypes.TryParse(text, out var type));
        Assert.Equal(expected, type);
    }

    [Theory]
    [InlineData("")]
    [InlineData("oracle")]
    [InlineData(null)]
    /*
     * "SqlServer" is here on purpose. That engine was in this enum and
     * has been taken back out, so a settings file written while it was
     * there must not name a provider this build cannot serve -- and it
     * must not fail to load either, which a throw here would cause.
     */
    [InlineData("SqlServer")]
    [InlineData("mssql")]
    public void Unknown_engine_names_are_refused(string? text)
    {
        Assert.False(DatabaseTypes.TryParse(text, out _));
    }

    [Theory]
    [InlineData(DatabaseType.Firebird, 3050)]
    [InlineData(DatabaseType.PostgreSql, 5432)]
    public void Each_engine_has_its_own_usual_port(DatabaseType type, int port)
    {
        Assert.Equal(port, type.DefaultPort());
    }

    [Fact]
    public void A_firebird_key_is_the_key_it_always_was()
    {
        var firebird = new DatabaseConfig
        {
            Server = "Host",
            Port = 3050,
            Username = "SYSDBA",
            Database = "/data/a.fdb"
        };

        // Saved rows hold this exact text; changing it would stop them
        // matching themselves.
        Assert.Equal("host|3050|sysdba|/data/a.fdb", firebird.ConnectionKey);
    }

    [Fact]
    public void The_same_host_and_port_for_another_engine_is_another_connection()
    {
        var firebird = new DatabaseConfig { Server = "h", Port = 5432, Username = "u", Database = "d" };
        var postgres = new DatabaseConfig
        {
            Type = DatabaseType.PostgreSql, Server = "h", Port = 5432, Username = "u", Database = "d"
        };

        Assert.NotEqual(firebird.ConnectionKey, postgres.ConnectionKey);
    }

    [Fact]
    public void The_engine_survives_being_saved_and_read_back()
    {
        using var root = new TempDataRoot();
        var database = root.OpenDatabase();

        database.AddConnection(new DatabaseConfig
        {
            Name = "Shop",
            Type = DatabaseType.PostgreSql,
            Server = "10.0.0.5",
            Port = 5432,
            Username = "app",
            Password = "secret",
            Database = "shop"
        });

        Assert.Equal(DatabaseType.PostgreSql, database.GetConnections().Single().Type);

        var edited = database.GetConnections().Single();
        edited.Type = DatabaseType.Firebird;
        edited.Port = 3051;
        database.UpdateConnection(edited);

        Assert.Equal(DatabaseType.Firebird, database.GetConnections().Single().Type);
    }

    [Fact]
    public void A_settings_file_from_before_engines_opens_as_firebird()
    {
        using var root = new TempDataRoot();

        // Create the current layout, then take the column away and put a
        // row in, which is what an older release left behind.
        root.OpenDatabase();

        SqliteConnection.ClearAllPools();

        using (var raw = new SqliteConnection($"Data Source={root.CurrentDatabasePath}"))
        {
            raw.Open();

            using var command = raw.CreateCommand();

            command.CommandText = """
                ALTER TABLE Databases DROP COLUMN DatabaseType;
                INSERT INTO Databases
                    (Id, Name, Server, Port, Username, Password, DatabaseValue, ConnectionKey)
                VALUES
                    ('old-1', 'Legacy', 'srv', 3050, 'SYSDBA', 'pw', '/d/old.fdb', 'srv|3050|sysdba|/d/old.fdb');
                """;

            command.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();

        var legacy = root.OpenDatabase().GetConnections().Single();

        Assert.Equal("Legacy", legacy.Name);
        Assert.Equal(DatabaseType.Firebird, legacy.Type);
    }

    /* ---- the engines refuse what they should ---- */

    /*
     * SQL Server's own guard tests were here and are gone with the
     * engine. What they covered -- INTO, a second statement after a
     * semicolon, OPENROWSET/OPENQUERY, xp_ procedures -- has no
     * equivalent on either remaining engine, because neither needs a
     * text guard to be safe. Each holds a read-only transaction that
     * the engine itself enforces, so a statement that fooled
     * IsReadOnlyStatement would still be refused by the database.
     */

    /*
     * The PostgreSQL guard, on its own, without a server.
     *
     * It exists because the read-only transaction is not enough on this
     * engine: Npgsql sends a semicolon-separated text as one batch, so
     * a COMMIT in the middle ends the read-only transaction and whatever
     * follows runs in a new one that is not read-only. The text guard is
     * the only thing standing there, so it is tested on its own.
 */
    [Theory]
    [InlineData("SELECT 1; COMMIT; DELETE FROM Orders")]
    [InlineData("SELECT 1; DELETE FROM Orders")]
    [InlineData("SELECT 1;SELECT 2")]
    [InlineData("WITH c AS (SELECT Id FROM Orders) SELECT * FROM c; DROP TABLE Orders")]
    [InlineData("SELECT 1; BEGIN; INSERT INTO Orders (Id) VALUES (2)")]
    [InlineData("SELECT 1; ROLLBACK; UPDATE Orders SET Id = 2")]
    [InlineData("SELECT 1; PREPARE TRANSACTION 'x'")]
    public void PostgreSQL_takes_one_statement(string sql)
    {
        Assert.NotNull(PostgreSqlProvider.Instance.RejectQuery(sql));
    }

    [Theory]
    [InlineData("SELECT * FROM Orders")]
    [InlineData("SELECT * FROM Orders;")]
    [InlineData("  -- note" + "\n" + "SELECT Id FROM Orders WHERE Id = @id")]
    [InlineData("SELECT 'DELETE FROM Orders; DROP TABLE Orders' AS note")]
    [InlineData("SELECT /* ; */ Id FROM Orders")]
    [InlineData("SELECT \"weird;name\" FROM Orders")]
    [InlineData("SELECT Id FROM Orders WHERE Name = 'a;b'")]
    [InlineData("SELECT Id FROM Orders WHERE Id = $1")]
    public void PostgreSQL_lets_one_select_through(string sql)
    {
        Assert.Null(PostgreSqlProvider.Instance.RejectQuery(sql));
    }

    /*
     * One trailing semicolon is allowed, because a driver may add one.
     * Two are two: the rule reads as "exactly one" and has to be.
     * TrimEnd(';') used to strip every trailing one, so this was let
     * through while the comment said otherwise -- harmless, since the
     * extra statements are empty and cannot write, but a documented
     * rule that is not the enforced one is a trap for whoever quotes it.
     */
    [Fact]
    public void PostgreSQL_allows_one_trailing_semicolon_and_no_more()
    {
        Assert.Null(
            PostgreSqlProvider.Instance.RejectQuery("SELECT 1;"));

        Assert.NotNull(
            PostgreSqlProvider.Instance.RejectQuery("SELECT 1;;"));
    }

    /*
     * A dollar-quoted body is a string, so a semicolon inside one is
     * part of the text rather than a second statement.
     */
    [Fact]
    public void PostgreSQL_skips_a_dollar_quoted_body()
    {
        // One statement, with a semicolon inside the body.
        Assert.Null(
            PostgreSqlProvider.Instance.RejectQuery(
                "CREATE FUNCTION f() RETURNS int AS $$ BEGIN RETURN 1; END $$ LANGUAGE plpgsql"));

        // The second semicolon is a real one, outside the body.
        Assert.NotNull(
            PostgreSqlProvider.Instance.RejectQuery(
                "SELECT $$a;b$$ UNION SELECT 1; DROP TABLE Orders"));
    }

    /*
     * Session-level advisory locks belong to the session, not the
     * transaction, so rolling back does not release them. A read request
     * that took one would leave it held against other clients.
     */
    [Theory]
    [InlineData("SELECT pg_advisory_lock(1)")]
    [InlineData("SELECT pg_try_advisory_lock(1)")]
    [InlineData("SELECT pg_advisory_lock_shared(2)")]
    [InlineData("SELECT pg_advisory_lock_exclusive(2)")]
    [InlineData("SELECT pg_try_advisory_lock_shared(2)")]
    [InlineData("SELECT pg_advisory_unlock(1)")]
    [InlineData("SELECT pg_advisory_unlock_shared(1)")]
    [InlineData("SELECT pg_advisory_unlock_all()")]
    [InlineData("SELECT * FROM f() WHERE pg_advisory_lock_shared(2)")]
    public void PostgreSQL_refuses_a_session_advisory_lock(string sql)
    {
        Assert.NotNull(PostgreSqlProvider.Instance.RejectQuery(sql));
    }

    /*
     * The transaction-scoped ones die with the transaction below, so
     * they are not the hazard and refusing them would only be noise.
     */
    [Theory]
    [InlineData("SELECT pg_advisory_xact_lock(1)")]
    [InlineData("SELECT pg_try_advisory_xact_lock_shared(1)")]
    public void PostgreSQL_allows_a_transaction_scoped_advisory_lock(string sql)
    {
        Assert.Null(PostgreSqlProvider.Instance.RejectQuery(sql));
    }

    [Theory]
    [InlineData("SELECT nextval('orders_id_seq')")]
    [InlineData("select setval('orders_id_seq', 10)")]
    [InlineData("SELECT NEXTVAL ( 'a' ) FROM generate_series(1, 3)")]
    public void A_postgres_sequence_cannot_be_moved_from_query(string sql)
    {
        Assert.True(FirebirdExecutor.AdvancesSequence(sql));
    }

    [Theory]
    [InlineData("SELECT currval('orders_id_seq')")]
    [InlineData("SELECT 'nextval(x)' AS note")]
    [InlineData("SELECT last_value FROM orders_id_seq")]
    public void Reading_a_postgres_sequence_is_fine(string sql)
    {
        Assert.False(FirebirdExecutor.AdvancesSequence(sql));
    }

    [Fact]
    public void Each_engine_builds_a_connection_string_with_its_own_port_and_database()
    {
        var config = new DatabaseConfig
        {
            Server = "db.internal",
            Port = 5544,
            Username = "app",
            Password = "p;w=d",
            Database = "shop"
        };

        var postgres = PostgreSqlProvider.BuildConnectionString(config);

        Assert.Contains("Host=db.internal", postgres);
        Assert.Contains("Port=5544", postgres);
        Assert.Contains("Database=shop", postgres);

        // A password with a semicolon in it must stay one value, which
        // is what building the string rather than concatenating it buys.
        Assert.Equal(
            "p;w=d",
            new Npgsql.NpgsqlConnectionStringBuilder(postgres).Password);

        var firebird = FirebirdProvider.BuildConnectionString(config);

        // Firebird's own builder writes "data source", lower case and
        // spaced, which is why this one reads it back through that
        // builder rather than matching the text.
        Assert.Contains(
            "db.internal",
            new FirebirdSql.Data.FirebirdClient.FbConnectionStringBuilder(
                firebird).DataSource);

        Assert.Contains(
            "shop",
            new FirebirdSql.Data.FirebirdClient.FbConnectionStringBuilder(
                firebird).Database);
    }

    [Fact]
    public async Task A_connection_that_cannot_open_reports_why_instead_of_throwing()
    {
        // Nothing listens on port 1 of the loopback address.
        var config = new DatabaseConfig
        {
            Type = DatabaseType.PostgreSql,
            Server = "127.0.0.1",
            Port = 1,
            Username = "u",
            Password = "p",
            Database = "d"
        };

        var (succeeded, error) = await ByteBridge.Data.DatabaseConnectionTester.TestAsync(config);

        Assert.False(succeeded);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }
}
