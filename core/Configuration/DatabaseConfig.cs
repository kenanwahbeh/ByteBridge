using System;

namespace ByteBridge.Configuration;

public class DatabaseConfig
{
    public string Id { get; set; } =
        Guid.NewGuid().ToString();

    public string Name { get; set; } =
        string.Empty;

    /* Firebird unless said otherwise, which is what every older settings file holds. */
    public DatabaseType Type { get; set; } =
        DatabaseType.Firebird;

    public string Server { get; set; } =
        "localhost";

    public int Port { get; set; } =
        3050;

    public string Username { get; set; } =
        "SYSDBA";

    public string Password { get; set; } =
        string.Empty;

    public string Database { get; set; } =
        string.Empty;

    public bool Enabled { get; set; } =
        true;

    public bool LastTestSuccessful { get; set; }

    public DateTime? LastTestedAt { get; set; }

    /*
     * Identifies the actual database connection.
     *
     * Name is intentionally NOT included. The engine is, except for
     * Firebird, because the same host and port can mean different things.
     * Password is intentionally NOT included.
     */
    public string ConnectionKey =>
        // Firebird keeps the key it always had, so rows saved before
        // there were other engines still match themselves.
        (Type == DatabaseType.Firebird ? string.Empty : $"{Type}|") +
        $"{Server.Trim().ToLowerInvariant()}|" +
        $"{Port}|" +
        $"{Username.Trim().ToLowerInvariant()}|" +
        $"{Database.Trim().ToLowerInvariant()}";
}