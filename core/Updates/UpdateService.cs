using System.Globalization;
using System.Reflection;
using ByteBridge.Data;
using ByteBridge.Enrollment;

namespace ByteBridge.Updates;

/*
 * What is known about updates, without asking anyone.
 *
 * Current is the running build. Latest is what the last successful check
 * found, and is null if there never was one. A failed check leaves the
 * previous answer in place and reports why in Error, so a laptop that is
 * offline does not lose the banner it already had.
 */
public sealed record UpdateStatus(
    ReleaseVersion Current,
    ReleaseVersion? Latest,
    string? Url,
    DateTimeOffset? CheckedAt,
    string? Error = null)
{
    public bool Available => Latest is { } latest && latest > Current;
}

/*
 * The check, the setting that turns it off, and the memory of the last
 * answer. The service, the command line and the control panel all go
 * through this, and share the answer through the settings file the same
 * way they share everything else: there is no channel between them, so
 * whichever checked last has told the others.
 */
public sealed class UpdateService
{
    /*
     * How long an answer is good for. Releases here are weeks apart, so
     * once a day is already generous, and it keeps a fleet of gateways
     * well inside GitHub's anonymous allowance.
     */
    public static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    internal const string CheckKey = "Updates.Check";

    internal const string CheckedAtKey = "Updates.CheckedAt";

    internal const string LatestKey = "Updates.Latest";

    internal const string UrlKey = "Updates.Url";

    private readonly SqliteDatabase _database;

    private readonly UpdateChecker _checker;

    private readonly IClock _clock;

    public UpdateService(
        SqliteDatabase database,
        UpdateChecker checker,
        IClock clock,
        ReleaseVersion current)
    {
        _database = database;
        _checker = checker;
        _clock = clock;
        Current = current;
    }

    public ReleaseVersion Current { get; }

    /*
     * The running build. The informational version is preferred because
     * it keeps a pre-release suffix, which the numeric assembly version
     * cannot; the SDK appends "+<commit>", which parsing drops.
     */
    public static ReleaseVersion CurrentVersion()
    {
        var assembly = typeof(UpdateService).Assembly;

        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (ReleaseVersion.TryParse(informational, out var version))
        {
            return version;
        }

        var numeric = assembly.GetName().Version;

        return numeric == null
            ? new ReleaseVersion(0, 0, 0)
            : new ReleaseVersion(
                numeric.Major,
                numeric.Minor,
                Math.Max(numeric.Build, 0));
    }

    public static bool IsEnabled(SqliteDatabase database) =>
        database.GetSetting(CheckKey) != "0";

    public static void SetEnabled(SqliteDatabase database, bool enabled) =>
        database.SetSetting(CheckKey, enabled ? "1" : "0");

    public bool Enabled => IsEnabled(_database);

    public UpdateStatus Cached(string? error = null) =>
        Read(_database, Current, error);

    /*
     * Reads the remembered answer. Shared by the gateway, which reports
     * it on /stats and never goes to the network itself.
     */
    public static UpdateStatus Read(
        SqliteDatabase database,
        ReleaseVersion current,
        string? error = null)
    {
        ReleaseVersion? latest = null;

        if (ReleaseVersion.TryParse(database.GetSetting(LatestKey), out var v))
        {
            latest = v;
        }

        DateTimeOffset? checkedAt = null;

        if (DateTimeOffset.TryParse(
                database.GetSetting(CheckedAtKey),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var at))
        {
            checkedAt = at;
        }

        var url = database.GetSetting(UrlKey);

        if (url == null
            || !url.StartsWith(
                UpdateChecker.RepositoryUrl,
                StringComparison.Ordinal))
        {
            url = null;
        }

        return new UpdateStatus(current, latest, url, checkedAt, error);
    }

    /*
     * Checks when due. force is "the person asked": it ignores both the
     * switch and the interval, because someone pressing the button has
     * said what they want whatever the background setting is.
     */
    public async Task<UpdateStatus> CheckAsync(
        bool force,
        CancellationToken cancellationToken = default)
    {
        var cached = Cached();

        if (!force)
        {
            if (!Enabled)
            {
                return cached;
            }

            if (cached.CheckedAt is { } last
                && last <= _clock.Now
                && _clock.Now - last < Interval)
            {
                return cached;
            }
        }

        var started = _clock.Now;

        UpdateInfo info;

        try
        {
            info = await _checker.GetLatestAsync(cancellationToken);
        }
        catch (UpdateException error)
        {
            return Cached(error.Message);
        }

        // Another check (the service, the panel, the command line) may
        // have finished while this one was waiting on the network. Its
        // answer is newer than the question this one asked, so keep it.
        if (Cached().CheckedAt is { } committed && committed > started)
        {
            var kept = Cached();

            if (kept.Latest == info.Version)
            {
                LastInfo = info;
            }

            return kept;
        }

        _database.SetSetting(LatestKey, info.Version.ToString());
        _database.SetSetting(UrlKey, info.PageUrl);
        _database.SetSetting(
            CheckedAtKey,
            _clock.Now.ToString("O", CultureInfo.InvariantCulture));

        LastInfo = info;

        return Cached();
    }

    /*
     * The release the last successful check in this process read, with
     * its notes and files. Not persisted: the installer is only wanted
     * by the control panel, right after it has asked.
     */
    public UpdateInfo? LastInfo { get; private set; }
}
