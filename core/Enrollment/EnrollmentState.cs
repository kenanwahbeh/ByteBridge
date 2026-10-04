using System.Globalization;
using ByteBridge.Data;

namespace ByteBridge.Enrollment;

public enum Phase
{
    /* Enrolled and waiting for the approver, or approved but not yet connected. */
    Requested,

    /*
     * The connector is being installed, or the install did not finish.
     * Saved before the connector is touched, so `unenroll` removes it
     * even when this never reached Connected, and a retry may replace
     * the half-installed service, which is this enrolment's own.
     */
    Installing,

    /* The connector is installed. */
    Connected
}

/*
 * Everything this machine remembers about its enrolment.
 *
 * ClaimSecret is a credential: with it, and the deviceKey, anyone can
 * collect this device's tunnel token or replace the key ByteBalance
 * holds for it. It sits in the same settings file as the API key and the
 * database passwords, which is already restricted to administrators, so
 * it is no more exposed than they are. The tunnel token itself is never
 * stored: it is fetched, handed to cloudflared and dropped.
 */
public sealed record EnrollmentState(
    string Server,
    string DeviceKey,
    string ClaimSecret,
    string Name,
    string Email,
    Phase Phase,
    DateTimeOffset EnrolledAt,
    string? Hostname = null,
    DateTimeOffset? ConnectedAt = null,
    DateTimeOffset? KeySharedAt = null,

    /*
     * Whether this enrolment is the one that made the gateway require the
     * tunnel's Access token. Only then does `unenroll` take it away again;
     * an administrator who had already turned it on keeps it.
     */
    bool EdgeAccessOwned = false);

public interface IEnrollmentStore
{
    EnrollmentState? Load();

    /* First save. Fails when an enrolment is already stored. */
    void Create(EnrollmentState state);

    /*
     * Saves a change to the enrolment that was loaded. Fails, writing
     * nothing, when it has been removed or replaced since, so a slow
     * operation cannot bring back what `unenroll` just deleted.
     */
    void Save(EnrollmentState state);

    void Clear();
}

/*
 * Kept in the Settings table, under "Enrollment.", so there is one
 * place secrets live and the control panel and the service, which only
 * ever meet through that file, see the same state.
 */
public sealed class SettingsEnrollmentStore : IEnrollmentStore
{
    private const string Prefix = "Enrollment.";

    private readonly SqliteDatabase _database;

    public SettingsEnrollmentStore(SqliteDatabase database)
    {
        _database = database;
    }

    public EnrollmentState? Load()
    {
        var server = Get("Server");
        var deviceKey = Get("DeviceKey");
        var secret = Get("ClaimSecret");
        var email = Get("Email");

        /*
         * Any of these missing means nothing usable was ever saved. A
         * half-written set is not "not enrolled": treating it so would
         * mint a new secret over one the server may already hold the
         * hash of.
         */
        if (server == null && deviceKey == null && secret == null && email == null)
        {
            return null;
        }

        if (server == null || deviceKey == null || secret == null || email == null)
        {
            throw new EnrollmentException(
                "The enrolment settings are incomplete. Ask the ByteBalance "
                + "administrator to remove this device, then run `unenroll` "
                + "here and enrol again.");
        }

        return new EnrollmentState(
            Server: server,
            DeviceKey: deviceKey,
            ClaimSecret: secret,
            Name: Get("Name") ?? deviceKey,
            Email: email,
            Phase: Enum.TryParse<Phase>(Get("Phase"), out var phase)
                ? phase
                : Phase.Requested,
            EnrolledAt: Time(Get("EnrolledAt")) ?? DateTimeOffset.UtcNow,
            Hostname: Get("Hostname"),
            ConnectedAt: Time(Get("ConnectedAt")),
            KeySharedAt: Time(Get("KeySharedAt")),
            EdgeAccessOwned: Get("EdgeAccessOwned") == "1");
    }

    public void Create(EnrollmentState state)
    {
        if (!_database.SetSettingsIf(Prefix + "ClaimSecret", null, Pairs(state)))
        {
            throw new EnrollmentException(
                "An enrolment was started on this machine at the same time. "
                + "Run `status` to see it.");
        }
    }

    public void Save(EnrollmentState state)
    {
        if (!_database.SetSettingsIf(
                Prefix + "ClaimSecret",
                state.ClaimSecret,
                Pairs(state)))
        {
            throw new EnrollmentException(
                "The enrolment on this machine was removed or replaced while "
                + "this was running, so nothing was saved. Run `status` to "
                + "see where it stands.");
        }
    }

    private static List<(string Key, string Value)> Pairs(EnrollmentState state) =>
    [
        (Prefix + "Server", state.Server),
        (Prefix + "DeviceKey", state.DeviceKey),
        (Prefix + "ClaimSecret", state.ClaimSecret),
        (Prefix + "Name", state.Name),
        (Prefix + "Email", state.Email),
        (Prefix + "Phase", state.Phase.ToString()),
        (Prefix + "EnrolledAt", Format(state.EnrolledAt)),
        (Prefix + "Hostname", state.Hostname ?? string.Empty),
        (Prefix + "ConnectedAt", state.ConnectedAt is { } c ? Format(c) : string.Empty),
        (Prefix + "KeySharedAt", state.KeySharedAt is { } k ? Format(k) : string.Empty),
        (Prefix + "EdgeAccessOwned", state.EdgeAccessOwned ? "1" : "0")
    ];

    public void Clear() => _database.DeleteSettings(Prefix);

    private string? Get(string key)
    {
        var value = _database.GetSetting(Prefix + key);

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string Format(DateTimeOffset value) =>
        value.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset? Time(string? value) =>
        DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var parsed)
            ? parsed
            : null;
}
