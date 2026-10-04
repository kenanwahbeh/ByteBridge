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
    [InlineData("SQL Server", DatabaseType.SqlServer)]
    [InlineData("sql-server", DatabaseType.SqlServer)]
    [InlineData("mssql", DatabaseType.SqlServer)]
    public void Engine_names_are_understood(string text, DatabaseType expected)
    {
        Assert.True(DatabaseTypes.TryParse(text, out var type));
        Assert.Equal(expected, type);
    }

    [Theory]
    [InlineData("")]
    [InlineData("oracle")]
    [InlineData(null)]
    public void Unknown_engine_names_are_refused(string? text)
    {
        Assert.False(DatabaseTypes.TryParse(text, out _));
    }

    [Theory]
    [InlineData(DatabaseType.Firebird, 3050)]
    [InlineData(DatabaseType.PostgreSql, 5432)]
    [InlineData(DatabaseType.SqlServer, 1433)]
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
        edited.Type = DatabaseType.SqlServer;
        edited.Port = 1433;
        database.UpdateConnection(edited);

        Assert.Equal(DatabaseType.SqlServer, database.GetConnections().Single().Type);
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

    [Theory]
    [InlineData("SELECT * FROM Orders")]
    [InlineData("SELECT * FROM Orders;")]
    [InlineData("  -- note" + "\n" + "SELECT TOP 5 Id FROM Orders WHERE Id = @id")]
    [InlineData("WITH recent AS (SELECT Id FROM Orders) SELECT * FROM recent")]
    [InlineData("SELECT 'DELETE FROM Orders; DROP TABLE Orders' AS note")]
    [InlineData("SELECT [update], [into] FROM Audit")]
    [InlineData("SELECT \"delete\" FROM Audit")]
    [InlineData("SELECT updated_at, created_by FROM Orders")]
    [InlineData("SELECT /* insert into x */ 1")]
    public void SQL_Server_lets_an_ordinary_select_through(string sql)
    {
        Assert.Null(SqlServerProvider.Instance.RejectQuery(sql));
    }

    [Theory]
    [InlineData("WITH c AS (SELECT Id FROM Orders) DELETE FROM c")]
    [InlineData("WITH c AS (SELECT Id FROM Orders) UPDATE c SET Id = 1")]
    [InlineData("SELECT * INTO Copy FROM Orders")]
    [InlineData("SELECT 1; DROP TABLE Orders")]
    [InlineData("SELECT 1;SELECT 2")]
    [InlineData("SELECT * FROM OPENROWSET('SQLNCLI', 'x', 'SELECT 1')")]
    [InlineData("SELECT * FROM OPENQUERY(Linked, 'DELETE FROM t')")]
    [InlineData("SELECT 1 WAITFOR DELAY '00:00:30'")]
    [InlineData("SELECT NEXT VALUE FOR dbo.Seq")]
    public void SQL_Server_refuses_what_a_select_can_still_smuggle_in(string sql)
    {
        // The sequence case is caught by the shared sequence guard, which
        // the gateway runs for every engine; the rest by the engine's own.
        var refused = SqlServerProvider.Instance.RejectQuery(sql) != null
            || FirebirdExecutor.AdvancesSequence(sql);

        Assert.True(refused, sql);
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

        config.Port = 1444;

        var sqlServer = SqlServerProvider.BuildConnectionString(config);

        Assert.Contains("db.internal,1444", sqlServer);
        Assert.Contains("shop", sqlServer);

        // A password with a semicolon in it must stay one value.
        Assert.Contains("p;w=d", new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(sqlServer).Password);
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
