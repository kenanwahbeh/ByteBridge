using Microsoft.Data.Sqlite;
using Xunit;
using ByteBridge.Configuration;
using ByteBridge.Data;
using ByteBridge.Security;

namespace ByteBridge.Tests;

/*
 * The engine column and secret protection were two separate pieces of
 * work that landed in the same file, and the only thing that could go
 * wrong between them is that one overwrite the other.
 *
 * These cover that seam: a connection with an engine must keep its
 * password wrapped, a file written before either feature must come
 * through both migrations intact, and a row naming an engine this
 * build has no provider for must not take the rest of the file with it.
 */
public class EngineAndSecretsTests
{
    private const string Password = "e!ngine-p4ssw0rd";
    private const string LegacyPassword = "legacy-plaintext-password";
    private const string LegacyApiKey = "legacy-plaintext-api-key";

    [Fact]
    public void A_PostgreSql_password_is_wrapped_like_any_other()
    {
        using var root = new TempDataRoot();

        var database = root.OpenDatabase();

        database.AddConnection(Postgres());

        /*
         * The engine is not a reason to store a secret differently. If
         * this ever says otherwise, the answer is a bug and not a
         * design: the read-only guard is per engine, the secret store
         * is not.
         */
        var stored = RawValue(
            root.CurrentDatabasePath,
            "SELECT Password FROM Databases;");

        Assert.StartsWith(SecretProtector.Prefix, stored);
        Assert.DoesNotContain(Password, stored);

        Assert.Equal(
            Password,
            database.GetConnections().Single().Password);

        Assert.Equal(DatabaseType.PostgreSql,
            database.GetConnections().Single().Type);
    }

    [Fact]
    public void A_file_from_before_either_feature_migrates_to_both()
    {
        using var root = new TempDataRoot();

        /*
         * The oldest shape: no engine column, a password in plain text,
         * an API key in plain text. What a v3.0.0 machine left behind
         * before either of them existed.
         */
        var database = root.OpenDatabase();

        // Mints the API key row the raw UPDATE below rewrites.
        database.GetGatewayConfig();

        database.AddConnection(Firebird());

        RawExecute(
            root.CurrentDatabasePath,
            $"UPDATE Databases SET Password = '{LegacyPassword}';");

        RawExecute(
            root.CurrentDatabasePath,
            "ALTER TABLE Databases DROP COLUMN DatabaseType;");

        RawExecute(
            root.CurrentDatabasePath,
            $"UPDATE Settings SET SettingValue = '{LegacyApiKey}' " +
            "WHERE SettingKey = 'Gateway.ApiKey';");

        RawExecute(
            root.CurrentDatabasePath,
            "PRAGMA wal_checkpoint(TRUNCATE);");

        var reopened = root.OpenDatabase();

        var connection = reopened.GetConnections().Single();

        // The password survived the migration, wrapped.
        Assert.Equal(LegacyPassword, connection.Password);
        Assert.StartsWith(
            SecretProtector.Prefix,
            RawValue(root.CurrentDatabasePath, "SELECT Password FROM Databases;"));

        // So did the engine column, defaulted to Firebird.
        Assert.Equal(DatabaseType.Firebird, connection.Type);

        // And the API key, which took the same trip.
        Assert.Equal(LegacyApiKey, reopened.GetGatewayConfig().ApiKey);

        Assert.Null(reopened.SecretsError);
    }

    [Fact]
    public void A_row_naming_an_engine_this_build_lacks_still_reads()
    {
        using var root = new TempDataRoot();

        var database = root.OpenDatabase();

        database.AddConnection(Postgres());

        var second = Firebird();

        // Names are unique: a request picks a connection by name, so two
        // of them cannot share one.
        second.Name = "Archive";

        database.AddConnection(second);

        /*
         * "SqlServer" is a value a build with that engine wrote. This
         * one has no provider for it, and must not throw on it: a
         * settings file from a sibling build is a normal thing to open,
         * and one unreadable row must not take the other rows with it.
         */
        RawExecute(
            root.CurrentDatabasePath,
            "UPDATE Databases SET DatabaseType = 'SqlServer' " +
            "WHERE Name = 'Sales';");

        var reopened = root.OpenDatabase();

        var connections = reopened.GetConnections();

        Assert.Equal(2, connections.Count);

        // Unreadable engine, readable row: falls back rather than fails.
        var sales = connections.Single(c => c.Name == "Sales");

        Assert.Equal(DatabaseType.Firebird, sales.Type);

        // And the password is still wrapped, not handed back as text.
        Assert.Equal(Password, sales.Password);

        Assert.Null(reopened.SecretsError);
    }

    [Fact]
    public void An_unreadable_password_does_not_hide_the_engine()
    {
        using var root = new TempDataRoot();
        using var moved = new TempDataRoot();

        var database = new SqliteDatabase(
            root.Path,
            new AesKeyFileSecretProtector(root.Path));

        database.AddConnection(Postgres());

        SqliteConnection.ClearAllPools();

        // The file moves without the key that opens it.
        var movedFolder =
            System.IO.Path.Combine(moved.Path, "ByteBridge");

        Directory.CreateDirectory(movedFolder);

        File.Copy(
            root.CurrentDatabasePath,
            System.IO.Path.Combine(movedFolder, "bytebridge.db"));

        var copy = new SqliteDatabase(
            moved.Path,
            new AesKeyFileSecretProtector(movedFolder));

        var connection = copy.GetConnections().Single();

        // Password lost and reported; the engine is still known, so the
        // panel can still say which database needs repairing.
        Assert.Equal(string.Empty, connection.Password);
        Assert.Equal(DatabaseType.PostgreSql, connection.Type);
        Assert.NotNull(copy.SecretsError);
    }

    [Fact]
    public void Two_engines_on_one_host_and_port_are_two_connections()
    {
        using var root = new TempDataRoot();

        var database = root.OpenDatabase();

        database.AddConnection(Firebird());

        var postgres = Postgres();

        postgres.Name = "Archive";

        database.AddConnection(postgres);

        Assert.Equal(2, database.GetConnections().Count);
    }

    [Fact]
    public void Changing_only_the_engine_is_not_a_duplicate()
    {
        using var root = new TempDataRoot();

        var database = root.OpenDatabase();

        database.AddConnection(Firebird());

        var same = Postgres();

        same.Id = database.GetConnections().Single().Id;

        database.UpdateConnection(same);

        Assert.Equal(DatabaseType.PostgreSql,
            database.GetConnections().Single().Type);
    }

    [Fact]
    public void The_engine_survives_a_password_edit()
    {
        using var root = new TempDataRoot();

        var database = root.OpenDatabase();

        database.AddConnection(Postgres());

        var connection = database.GetConnections().Single();

        connection.Password = "rotated";
        connection.Name = "Shop";

        database.UpdateConnection(connection);

        var saved = database.GetConnections().Single();

        Assert.Equal("Shop", saved.Name);
        Assert.Equal(DatabaseType.PostgreSql, saved.Type);
        Assert.Equal("rotated", saved.Password);

        // The old password is not sitting in the file beside the new one.
        Assert.DoesNotContain(
            Password,
            RawValue(
                root.CurrentDatabasePath,
                "SELECT Password FROM Databases WHERE Name = 'Shop';"));
    }

    [Fact]
    public void Connection_keys_of_different_engines_do_not_collide()
    {
        var firebird = Firebird();
        var postgres = Postgres();

        Assert.NotEqual(firebird.ConnectionKey, postgres.ConnectionKey);

        // And Firebird's is unchanged, so rows saved before engines
        // existed still match themselves.
        Assert.Equal(
            "localhost|3050|sysdba|/data/sales.fdb",
            firebird.ConnectionKey);
    }

    private static DatabaseConfig Firebird() =>
        new()
        {
            Name = "Sales",
            Type = DatabaseType.Firebird,
            Server = "localhost",
            Port = 3050,
            Username = "SYSDBA",
            Password = Password,
            Database = "/data/sales.fdb"
        };

    private static DatabaseConfig Postgres() =>
        new()
        {
            Name = "Sales",
            Type = DatabaseType.PostgreSql,
            Server = "localhost",
            Port = 5432,
            Username = "postgres",
            Password = Password,
            Database = "shop"
        };

    private static string? RawValue(string path, string sql)
    {
        using var connection =
            new SqliteConnection($"Data Source={path};Pooling=false");

        connection.Open();

        using var command = connection.CreateCommand();

        command.CommandText = sql;

        return command.ExecuteScalar() as string;
    }

    private static void RawExecute(string path, string sql)
    {
        using var connection =
            new SqliteConnection($"Data Source={path};Pooling=false");

        connection.Open();

        using var command = connection.CreateCommand();

        command.CommandText = sql;

        command.ExecuteNonQuery();
    }
}