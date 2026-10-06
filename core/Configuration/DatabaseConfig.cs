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

    /*
     * Whether this build can actually serve that engine.
     *
     * False only for a row whose stored engine name is not one this
     * build has a provider for -- a settings file written by a build
     * that had it, read by one that does not. Such a row keeps its
     * details so the window can show and repair it, but nothing may be
     * dispatched through it: a Firebird client talking to a PostgreSQL
     * endpoint fails in a way that looks like the database is down
     * rather than the engine being unavailable, and a health probe
     * would keep marking it broken for that reason.
     *
     * So Type stays Firebird for display, and this is what the gateway
     * and the tester check before choosing a provider.
     */
    public bool EngineIsSupported { get; set; } = true;

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
     * Name is intentionally NOT included.
     * Password is intentionally NOT included.
     *
     * The engine is, except for Firebird, because the same host and
     * port can mean different things to different engines. Firebird
     * keeps the key it has always had, so a row saved before there
     * were other engines still matches itself.
     */
    public string ConnectionKey =>
        (Type == DatabaseType.Firebird && EngineIsSupported
            ? string.Empty
            : $"{(EngineIsSupported ? Type.ToString() : "unsupported")}|") +
        $"{Server.Trim().ToLowerInvariant()}|" +
        $"{Port}|" +
        $"{Username.Trim().ToLowerInvariant()}|" +
        $"{Database.Trim().ToLowerInvariant()}";
}