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

    /*
     * Whether the gateway is serving this connection now. This is the
     * ONE place that decides, because four surfaces report it: the
     * control panel's card, /health's online count, /databases, and
     * ByteBalanceTech's companion agent, which reads /health. A second
     * derivation is how an unsupported connection came to be counted
     * online on some of them and not others.
     *
     * Use IsOnline rather than Enabled && LastTestSuccessful. There is
     * no exception; a surface that needs one has to say so here.
     *
     * The engine is part of it because a row this build cannot serve is
     * not an online database: requests for it are refused with 409 and
     * the health probe skips it, so counting it would report a
     * connection that answers nothing. Its LastTestSuccessful may well
     * be true from before the engine was taken away, and a stored
     * result from the past is not the present.
     */
    public bool IsOnline =>
        Enabled &&
        EngineIsSupported &&
        LastTestSuccessful;

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