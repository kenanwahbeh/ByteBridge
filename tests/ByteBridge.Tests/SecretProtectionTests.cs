using System.Text;
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
    public void The_Key_Lives_Outside_The_Data_Folder()
    {
        using var root = new TempDataRoot();

        var database = root.OpenDatabase();

        database.GetGatewayConfig();

        var dataFolder =
            System.IO.Path.Combine(root.Path, "ByteBridge");

        /*
         * Whatever the OS, nothing named .secrets-key may sit beside
         * bytebridge.db: a copy of the data folder must not carry the
         * key that opens it. On Linux the key is a sibling directory
         * away, owner-only, with an owner-read/write file inside.
         */
        Assert.False(
            File.Exists(
                System.IO.Path.Combine(
                    dataFolder, ".secrets-key")));

        if (!OperatingSystem.IsWindows())
        {
            var keyFile = System.IO.Path.Combine(
                root.Path, "bytebridge-keys", ".secrets-key");

            Assert.True(File.Exists(keyFile));

            Assert.Equal(
                UnixFileMode.UserRead | UnixFileMode.UserWrite,
                File.GetUnixFileMode(keyFile));
        }
    }

    [Fact]
    public void Migrating_Leaves_No_Plaintext_In_Any_Database_File()
    {
        const string LegacyPassword = "legacy-plaintext-password";
        const string LegacyApiKey = "legacy-plaintext-api-key";
        const string LegacyClaimSecret = "legacy-plaintext-claim-secret";

        using var root = new TempDataRoot();

        var database = root.OpenDatabase();

        database.AddConnection(SampleConnection());
        database.GetGatewayConfig();

        /*
         * The state a version from before protection existed would
         * have left: every secret readable in the file itself, not
         * only in the log. Each row is seeded first -- the claim
         * secret in particular is written by the enrolment flow
         * rather than the panel, so nothing has written a row there
         * yet -- and then the log is checkpointed into the main file,
         * so what this test hunts for afterwards is the
         * leftover-in-a-page case rather than only the log.
         */
        RawExecute(
            root.CurrentDatabasePath,
            $"UPDATE Databases SET Password = '{LegacyPassword}';");

        RawExecute(
            root.CurrentDatabasePath,
            $"UPDATE Settings SET SettingValue = '{LegacyApiKey}' " +
            "WHERE SettingKey = 'Gateway.ApiKey';");

        RawExecute(
            root.CurrentDatabasePath,
            "INSERT INTO Settings (SettingKey, SettingValue) " +
            $"VALUES ('Enrollment.ClaimSecret', '{LegacyClaimSecret}');");

        RawExecute(
            root.CurrentDatabasePath,
            "PRAGMA wal_checkpoint(TRUNCATE);");

        Assert.Contains(
            LegacyPassword,
            AllDatabaseText(root.CurrentDatabasePath),
            StringComparison.Ordinal);

        // Opens, migrates, and scrubs.
        var reopened = root.OpenDatabase();

        Assert.Equal(
            LegacyPassword,
            reopened.GetConnections().Single().Password);

        Assert.Equal(
            LegacyApiKey,
            reopened.GetGatewayConfig().ApiKey);

        Assert.Equal(
            LegacyClaimSecret,
            reopened.GetSetting("Enrollment.ClaimSecret"));

        /*
         * The migrated values still work, and the plaintext they
         * replaced is in none of the three files -- not the database,
         * not the write-ahead log, not the shared-memory index.
         */
        SqliteConnection.ClearAllPools();

        foreach (var plaintext in new[]
                 {
                     LegacyPassword,
                     LegacyApiKey,
                     LegacyClaimSecret
                 })
        {
            Assert.DoesNotContain(
                plaintext,
                AllDatabaseText(root.CurrentDatabasePath),
                StringComparison.Ordinal);
        }

        // And the log was truncated rather than left holding frames.
        var wal = root.CurrentDatabasePath + "-wal";

        Assert.True(
            !File.Exists(wal) || new FileInfo(wal).Length == 0);
    }

    /*
     * Every file SQLite may have written, as text: the database, the
     * write-ahead log and the shared-memory index, each if it exists.
     * Latin-1 so a byte-for-byte reading does not throw on whatever
     * the files happen to contain.
     */
    private static string AllDatabaseText(string databasePath)
    {
        var files = new List<string> { databasePath };

        foreach (var suffix in new[] { "-wal", "-shm" })
        {
            files.Add(databasePath + suffix);
        }

        var text = new StringBuilder();

        foreach (var file in files)
        {
            if (!File.Exists(file))
            {
                continue;
            }

            text.Append(
                System.Text.Encoding.Latin1.GetString(
                    File.ReadAllBytes(file)));
        }

        return text.ToString();
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
