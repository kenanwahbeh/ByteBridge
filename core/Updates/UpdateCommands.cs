using System.Globalization;
using ByteBridge.Data;
using ByteBridge.Enrollment;

namespace ByteBridge.Updates;

/*
 * "update" on the command line.
 *
 * Checking is all it does. Installing a new build on a machine that sits
 * in front of databases is a decision for the person who runs it, and
 * the two platforms already have the right tool for it: the Windows
 * installers, which the control panel offers, and apt on Linux, which
 * verifies what it installs against the repository key.
 */
public static class UpdateCommands
{
    public const string Usage = """
        update [check]         Look for a newer release now
        update status          Show what the last check found, without
                               going to the network
        update on | off        Whether the service looks for a newer release
                               by itself, about once a day (on by default)
        """;

    public static bool Handles(string verb) =>
        verb.Equals("update", StringComparison.OrdinalIgnoreCase);

    public static int Run(
        string[] args,
        SqliteDatabase database,
        UpdateService? service = null,
        TextWriter? output = null,
        TextWriter? error = null)
    {
        var @out = output ?? Console.Out;
        var err = error ?? Console.Error;

        var action = args.Length > 1 ? args[1].ToLowerInvariant() : "check";

        if (args.Length > 2)
        {
            err.WriteLine($"error: update does not understand '{args[2]}'.");
            return 1;
        }

        switch (action)
        {
            case "on":
                UpdateService.SetEnabled(database, true);

                @out.WriteLine(
                    "Update checks are on. The service looks for a newer "
                    + "release about once a day. It never installs one.");
                return 0;

            case "off":
                UpdateService.SetEnabled(database, false);

                @out.WriteLine(
                    "Update checks are off. Nothing is sent to GitHub "
                    + "unless you run: update check");
                return 0;

            case "status":
                Describe(
                    @out,
                    UpdateService.Read(
                        database,
                        service?.Current ?? UpdateService.CurrentVersion()),
                    UpdateService.IsEnabled(database));
                return 0;

            case "check":
                return Check(database, service, @out, err);

            default:
                err.WriteLine(
                    "error: update takes 'check', 'status', 'on' or 'off'.");
                return 1;
        }
    }

    private static int Check(
        SqliteDatabase database,
        UpdateService? service,
        TextWriter output,
        TextWriter error)
    {
        var current = service?.Current ?? UpdateService.CurrentVersion();

        service ??= new UpdateService(
            database,
            new UpdateChecker(UpdateChecker.CreateHttpClient(), current),
            new SystemClock(),
            current);

        var status = service.CheckAsync(force: true).GetAwaiter().GetResult();

        // A failed check leaves the old answer in place and says why.
        if (status.Error != null)
        {
            error.WriteLine("error: " + status.Error);
            return 1;
        }

        Describe(output, status, UpdateService.IsEnabled(database));
        return 0;
    }

    private static void Describe(
        TextWriter output,
        UpdateStatus status,
        bool enabled)
    {
        output.WriteLine($"Installed:  {status.Current}");

        if (status.Latest is not { } latest)
        {
            output.WriteLine("Latest:     not checked yet. Run: update check");
        }
        else
        {
            var when = status.CheckedAt is { } at
                ? " (checked "
                    + at.UtcDateTime.ToString(
                        "yyyy-MM-dd HH:mm 'UTC'",
                        CultureInfo.InvariantCulture)
                    + ")"
                : "";

            output.WriteLine($"Latest:     {latest}{when}");
        }

        output.WriteLine(
            "Checks:     "
            + (enabled
                ? "on, about once a day"
                : "off, only when you run: update check"));

        if (!status.Available)
        {
            if (status.Latest != null)
            {
                output.WriteLine();
                output.WriteLine("ByteBridge is up to date.");
            }

            return;
        }

        output.WriteLine();
        output.WriteLine($"A newer version is available: {status.Latest}");

        if (status.Url != null)
        {
            output.WriteLine($"Release notes: {status.Url}");
        }

        output.WriteLine();
        output.WriteLine(HowToUpdate());
    }

    /*
     * Where the new build comes from depends on how this one arrived.
     * A package-managed install is updated by its package manager; the
     * tarball by its installer script; Windows by the control panel.
     */
    internal static string HowToUpdate(
        bool? windows = null,
        bool? packaged = null)
    {
        if (windows ?? OperatingSystem.IsWindows())
        {
            return "To update: open the ByteBridge control panel, which "
                + "offers it,\nor download the installer from the release "
                + "page and run it. Your settings are kept.";
        }

        if (packaged ?? File.Exists("/var/lib/dpkg/info/bytebridge.list"))
        {
            return "To update:  sudo apt update && "
                + "sudo apt install --only-upgrade bytebridge";
        }

        return "To update: download the new linux-x64 tarball from the "
            + "release page and\nrun install.sh from it again. Your "
            + "settings are kept.";
    }
}
