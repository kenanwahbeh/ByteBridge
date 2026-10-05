using Microsoft.Data.Sqlite;
using Xunit;
using ByteBridge.Configuration;
using ByteBridge.Data;
using ByteBridge.Security;

namespace ByteBridge.Tests;

/*
 * The secrets in bytebridge.db are wrapped at rest. These cover the
 * wrapper itself (round-trip, legacy passthrough, no double-wrapping),
 * the storage layer (passwords and the API key land wrapped and read
 * back plain), the migration of files written before wrapping existed,
 * and the moved-to-another-machine case, which must fail closed.
 */
public class SecretProtectionTests
{
    private const string Secret =
        "s3cret! with spaces and رموز";

    [Fact]
    public void Protector_RoundTrips_On_This_Machine()
    {
        using var root = new TempDataRoot();

        var protector = SecretProtector.ForMachine(root.Path);

        var stored = protector.Protect(Secret);

        Assert.StartsWith(SecretProtector.Prefix, stored);
        Assert.DoesNotContain(Secret, stored);
        Assert.Equal(Secret, protector.Unprotect(stored));
    }

    [Fact]
    public void Unprotect_Passes_Legacy_Plaintext_Through()
    {
        using var root = new TempDataRoot();

        var protector = SecretProtector.ForMachine(root.Path);

        Assert.Equal("masterkey", protector.Unprotect("masterkey"));
        Assert.True(protector.TryUnprotect("masterkey", out var plain));
        Assert.Equal("masterkey", plain);
    }

    [Fact]
    public void Protect_Never_Wraps_Twice()
    {
        using var root = new TempDataRoot();

        var protector = SecretProtector.ForMachine(root.Path);

        var once = protector.Protect(Secret);

        Assert.Equal(once, protector.Protect(once));
    }

    [Fact]
    public void Equal_Secrets_Do_Not_Protect_To_Equal_Text()
    {
        using var root = new TempDataRoot();

        var protector = SecretProtector.ForMachine(root.Path);

        /*
         * A fresh nonce per call, so a settings file cannot reveal
         * that two connections share a password.
         */
        Assert.NotEqual(
            protector.Protect(Secret),
            protector.Protect(Secret));
    }

    [Fact]
    public void Connection_Password_Lands_Wrapped_And_Reads_Back_Plain()
    {
        using var root = new TempDataRoot();

        var database = root.OpenDatabase();

        database.AddConnection(SampleConnection());

        var stored = RawQuery(
            root.CurrentDatabasePath,
            "SELECT Password FROM Databases;");

        Assert.StartsWith(SecretProtector.Prefix, stored);
        Assert.DoesNotContain(Secret, stored);

        var reopened = root.OpenDatabase();

        Assert.Equal(
            Secret,
            reopened.GetConnections().Single().Password);
    }

    [Fact]
    public void Api_Key_Lands_Wrapped_And_Reads_Back_Plain()
    {
        using var root = new TempDataRoot();

        string apiKey;

        var database = root.OpenDatabase();

        apiKey = database.GetGatewayConfig().ApiKey;

        Assert.False(string.IsNullOrEmpty(apiKey));
        Assert.False(SecretProtector.HasPrefix(apiKey));

        var stored = RawQuery(
            root.CurrentDatabasePath,
            "SELECT SettingValue FROM Settings " +
            "WHERE SettingKey = 'Gateway.ApiKey';");

        Assert.StartsWith(SecretProtector.Prefix, stored);
        Assert.DoesNotContain(apiKey, stored);

        var reopened = root.OpenDatabase();

        Assert.Equal(apiKey, reopened.GetGatewayConfig().ApiKey);
    }

    [Fact]
    public void Claim_Secret_Lands_Wrapped()
    {
        using var root = new TempDataRoot();

        var database = root.OpenDatabase();

        database.SetSetting("Enrollment.ClaimSecret", Secret);

        var stored = RawQuery(
            root.CurrentDatabasePath,
            "SELECT SettingValue FROM Settings " +
            "WHERE SettingKey = 'Enrollment.ClaimSecret';");

        Assert.StartsWith(SecretProtector.Prefix, stored);

        var reopened = root.OpenDatabase();

        Assert.Equal(
            Secret,
            reopened.GetSetting("Enrollment.ClaimSecret"));
    }

    [Fact]
    public void A_File_From_Before_Wrapping_Is_Migrated_On_Open()
    {
        using var root = new TempDataRoot();

        /*
         * Written raw, below SqliteDatabase, the way a version from
         * before wrapping existed would have left them.
         */
        var database = root.OpenDatabase();

        database.AddConnection(SampleConnection());

        // Mints the key row the raw UPDATE below then rewrites.
        database.GetGatewayConfig();

        RawExecute(
            root.CurrentDatabasePath,
            $"UPDATE Databases SET Password = '{Secret}';");

        RawExecute(
            root.CurrentDatabasePath,
            "UPDATE Settings SET SettingValue = 'legacy-key-123' " +
            "WHERE SettingKey = 'Gateway.ApiKey';");

        var reopened = root.OpenDatabase();

        Assert.Equal(
            Secret,
            reopened.GetConnections().Single().Password);

        Assert.Equal(
            "legacy-key-123",
            reopened.GetGatewayConfig().ApiKey);

        Assert.Null(reopened.SecretsError);

        Assert.StartsWith(
            SecretProtector.Prefix,
            RawQuery(
                root.CurrentDatabasePath,
                "SELECT Password FROM Databases;"));

        Assert.StartsWith(
            SecretProtector.Prefix,
            RawQuery(
                root.CurrentDatabasePath,
                "SELECT SettingValue FROM Settings " +
                "WHERE SettingKey = 'Gateway.ApiKey';"));
    }

    [Fact]
    public void A_File_From_Another_Machine_Fails_Closed()
    {
        using var origin = new TempDataRoot();
        using var moved = new TempDataRoot();

        string originalKey;

        /*
         * The AES protector is used directly on both sides, whatever
         * the test OS: on Windows a copied file would still decrypt
         * under the same machine's DPAPI key, which is exactly the
         * case this test is not about.
         */
        var database = new SqliteDatabase(
            origin.Path,
            new AesKeyFileSecretProtector(origin.Path));

        database.AddConnection(SampleConnection());

        originalKey = database.GetGatewayConfig().ApiKey;

        var movedFolder =
            System.IO.Path.Combine(moved.Path, "ByteBridge");

        Directory.CreateDirectory(movedFolder);

        /*
         * The database is in WAL mode, so recent writes live in the
         * -wal sidecar until a checkpoint folds them into the main
         * file. Copying without one would bring an empty database.
         */
        SqliteConnection.ClearAllPools();

        RawExecute(
            origin.CurrentDatabasePath,
            "PRAGMA wal_checkpoint(TRUNCATE);");

        // Only the database moves; the key file stays behind.
        File.Copy(
            origin.CurrentDatabasePath,
            System.IO.Path.Combine(movedFolder, "bytebridge.db"));

        var copy = new SqliteDatabase(
            moved.Path,
            new AesKeyFileSecretProtector(movedFolder));

        /*
         * The password reads empty rather than wrong, and says so.
         * The API key is quietly re-minted so the gateway keeps
         * serving; nothing anywhere throws.
         */
        var connection = copy.GetConnections().Single();

        Assert.Equal(string.Empty, connection.Password);
        Assert.NotNull(copy.SecretsError);

        var reminted = copy.GetGatewayConfig().ApiKey;

        Assert.False(string.IsNullOrEmpty(reminted));
        Assert.NotEqual(originalKey, reminted);
    }

    [Fact]
    public void Non_Secret_Settings_Stay_Plain()
    {
        using var root = new TempDataRoot();

        var database = root.OpenDatabase();

        database.SetSetting("Gateway.Port", "8080");

        Assert.Equal(
            "8080",
            RawQuery(
                root.CurrentDatabasePath,
                "SELECT SettingValue FROM Settings " +
                "WHERE SettingKey = 'Gateway.Port';"));
    }

    private static DatabaseConfig SampleConnection() =>
        new()
        {
            Name = "Sales",
            Server = "127.0.0.1",
            Port = 3050,
            Username = "SYSDBA",
            Password = Secret,
            Database = "C:\\data\\sales.fdb"
        };

    private static string? RawQuery(string path, string sql)
    {
        using var connection =
            new SqliteConnection(
                $"Data Source={path};Pooling=false");

        connection.Open();

        using var command = connection.CreateCommand();

        command.CommandText = sql;

        return command.ExecuteScalar() as string;
    }

    private static void RawExecute(string path, string sql)
    {
        using var connection =
            new SqliteConnection(
                $"Data Source={path};Pooling=false");

        connection.Open();

        using var command = connection.CreateCommand();

        command.CommandText = sql;

        command.ExecuteNonQuery();
    }
}
