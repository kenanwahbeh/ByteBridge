using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using ByteBridge.Data;
using ByteBridge.Enrollment;
using ByteBridge.Updates;

namespace ByteBridge;

/*
 * The control panel's side of updating: asking, fetching the right
 * installer, and starting it.
 *
 * It does not decide anything about versions or checksums. That is the
 * Updates namespace in the core library, where the tests are; this adds
 * only what needs Windows -- which of the four installers this machine
 * was set up from, and the registry to find out.
 */
internal sealed class AppUpdates
{
    // The Setup program's AppId (installer/ByteBridge.iss) plus "_is1".
    private const string SetupUninstallKey =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\"
        + "{4DF9ABE1-3644-4B2B-9A42-B4742A9C6DB4}_is1";

    private static readonly HttpClient Http =
        UpdateChecker.CreateHttpClient();

    private readonly SqliteDatabase _database;

    public AppUpdates(SqliteDatabase database)
    {
        _database = database;

        var current = UpdateService.CurrentVersion();

        Service = new UpdateService(
            database,
            new UpdateChecker(Http, current),
            new SystemClock(),
            current);
    }

    public UpdateService Service { get; }

    /*
     * Whether this install carries its own .NET. The framework-dependent
     * editions do not, and have no coreclr.dll beside them.
     */
    internal static bool IsSelfContained() =>
        File.Exists(Path.Combine(AppContext.BaseDirectory, "coreclr.dll"));

    /*
     * The Setup program writes an uninstall entry under its AppId; the
     * MSI does not. If it is not there the machine was set up from the
     * MSI, and a Setup program run over it would register a second copy.
     */
    internal static InstallerKind InstalledFrom()
    {
        var running = Normalize(AppContext.BaseDirectory);

        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var key = hive.OpenSubKey(SetupUninstallKey);

            // An entry only counts when it is for the copy that is
            // running; a Setup install elsewhere says nothing about an
            // MSI install here.
            if (key?.GetValue("InstallLocation") is string location
                && Normalize(location) == running)
            {
                return InstallerKind.Setup;
            }
        }

        return InstallerKind.Msi;
    }

    private static string Normalize(string path) =>
        Path.GetFullPath(path).TrimEnd('\\', '/').ToUpperInvariant();

    /*
     * Inside the settings folder, which only SYSTEM and administrators
     * can write: the installer is checked here and run afterwards, and
     * in a folder any account could write, another process could swap
     * it in between.
     */
    private string DownloadFolder =>
        Path.Combine(_database.DataDirectory, "updates");

    /*
     * Downloads and verifies the installer for the newest release.
     * Returns null when the release does not carry one that matches
     * this install, in which case the caller should offer the release
     * page instead.
     */
    public async Task<string?> DownloadAsync(
        IProgress<double> progress,
        CancellationToken cancellationToken = default)
    {
        // The folder is only safe if securing the data directory worked.
        // Otherwise another account might replace the file between the
        // checksum and the run.
        if (_database.PermissionsError != null)
        {
            throw new UpdateException(
                "The settings folder could not be restricted to administrators, "
                + "so an installer will not be saved there.");
        }

        var release = Service.LastInfo;

        // What was announced is what gets installed: details left over
        // from an older check in this process are not trusted when
        // another check has since found a different release.
        if (release == null
            || release.Version <= Service.Current
            || release.Version != Service.Cached().Latest)
        {
            var status = await Service.CheckAsync(true, cancellationToken);

            if (status.Error != null)
            {
                throw new UpdateException(status.Error);
            }

            release = Service.LastInfo;
        }

        if (release == null
            || release.Version <= Service.Current
            || release.Version != Service.Cached().Latest)
        {
            return null;
        }

        var name = UpdateInstaller.AssetName(
            release.Version,
            IsSelfContained(),
            InstalledFrom());

        if (!release.Assets.Any(a => a.Name == name))
        {
            return null;
        }

        return await UpdateInstaller.DownloadAsync(
            Http,
            release,
            name,
            DownloadFolder,
            progress,
            cancellationToken);
    }

    /*
     * Starts the downloaded installer. The caller then exits, so the
     * installer is not left waiting on this window to close.
     */
    public static void Start(string installer)
    {
        var info = installer.EndsWith(".msi", StringComparison.OrdinalIgnoreCase)
            ? new ProcessStartInfo(
                "msiexec.exe",
                "/i \"" + installer + "\"")
            : new ProcessStartInfo(installer);

        info.UseShellExecute = true;

        Process.Start(info);
    }
}
