namespace ByteBridge.Configuration;

using ByteBridge.Gateway;

public class GatewayConfig
{
    /*
     * The gateway deliberately binds to loopback only.
     *
     * cloudflared runs on this same machine and reaches the
     * gateway over 127.0.0.1, so there is no reason to expose
     * the listener on the LAN. Binding to a non-loopback
     * address on Windows would also require an HTTP.SYS URL
     * reservation (netsh http add urlacl) or elevation.
     */
    public string Host { get; set; } = "127.0.0.1";

    public int Port { get; set; } = 8080;

    /*
     * Required on every endpoint except /health.
     *
     * Generated on first run and stored in the local SQLite
     * file. The tunnel makes this listener reachable from the
     * public internet, so the key is the only thing standing
     * between the internet and the Firebird databases.
     */
    public string ApiKey { get; set; } = string.Empty;

    public bool AutoStart { get; set; } = true;

    /*
     * Caps the number of rows a single /query may return so a
     * careless SELECT cannot pull an entire table through the
     * tunnel.
     */
    public int MaxRows { get; set; } = 1000;

    public int CommandTimeoutSeconds { get; set; } = 30;

    /*
     * How many wrong API keys one caller may send inside
     * AuthWindowSeconds before being refused for AuthBlockSeconds.
     * Zero turns the limit off.
     */
    public int AuthMaxFailures { get; set; } = 10;

    public int AuthWindowSeconds { get; set; } = 60;

    public int AuthBlockSeconds { get; set; } = 60;

    public string BaseUrl =>
        $"http://{Host}:{Port}";

    public string Prefix =>
        $"http://{Host}:{Port}/";

    /*
     * Whether a port is one anything can listen on.
     *
     * This test was written five times -- in the CLI twice, in the Web
     * Server dialog, in the gateway switch, in the wizard -- and a sixth
     * place got it wrong differently: SqliteDatabase keeps an
     * out-of-range port out of the file without saying anything, so a row
     * written by one surface and read by another could disagree about it.
     *
     * One answer, so that a sixth cannot be written.
     */
    public static bool IsValidPort(int port) =>
        port is >= 1 and <= 65535;

    /*
     * Whether the running listener has to be dropped and bound again for
     * these settings to take effect, as opposed to being read from the
     * live object on every request.
     *
     * A field list, and that is the hazard: add a setting to this class
     * and it is silently not on the list, so changing it does nothing at
     * all and nothing says so. The API key is the deliberate omission --
     * it is swapped into the running gateway without a rebind -- and
     * TestForRebind below is what keeps that omission from being an
     * accident.
     */
    public bool NeedsRebindFrom(GatewayConfig running) =>
        running.Host != Host
        || running.Port != Port
        || running.MaxRows != MaxRows
        || running.CommandTimeoutSeconds != CommandTimeoutSeconds
        || running.AuthMaxFailures != AuthMaxFailures
        || running.AuthWindowSeconds != AuthWindowSeconds
        || running.AuthBlockSeconds != AuthBlockSeconds;

    /*
     * The seven settings above, so that adding an eighth is a test
     * failure rather than a setting nobody can change.
     */
    public static string[] RebindSettings =>
    [
        nameof(Host),
        nameof(Port),
        nameof(MaxRows),
        nameof(CommandTimeoutSeconds),
        nameof(AuthMaxFailures),
        nameof(AuthWindowSeconds),
        nameof(AuthBlockSeconds)
    ];

    /*
     * Whether the gateway is meant to be listening and the service that
     * hosts it is up.
     *
     * Two windows asked this and got different answers: the main page
     * read the service state alone, so with the setting off and the
     * service running it offered to Stop a service nobody had asked to
     * start, while the dialog -- correctly reading "off" as off --
     * offered Turn On. Clicking that set AutoStart to true behind the
     * operator's back, undoing the choice they had just made.
     */
    public bool GatewayIsOn(ServiceState state) =>
        AutoStart && state == ServiceState.Running;

    /*
     * The lockout, in the unit the person set it in.
     *
     * Rounded up, because the panel's box says minutes and "0.5 minutes"
     * is what truncation of a 30-second block produces.
     */
    public int LockoutMinutes =>
        (AuthBlockSeconds + 59) / 60;

    public const int MaxLockoutAttempts = 10000;

    public const int MaxLockoutMinutes = 1440;

    /*
     * Whether the attempt limit is on at all. Zero is how the limiter is
     * turned off, and it cannot be expressed in minutes -- which is why
     * the panel disables the box but leaves its value in place, and why
     * that value must not be written back on the way past.
     */
    public bool LockoutIsEnabled =>
        AuthMaxFailures > 0;
}
