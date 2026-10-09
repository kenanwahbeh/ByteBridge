using System.Globalization;
using System.Security.Cryptography;

namespace ByteBridge.Updates;

public enum InstallerKind
{
    // The Inno Setup wizard: ByteBridge-x.y.z-x64[-framework]-setup.exe
    Setup,

    // The Windows Installer package: ByteBridge-x.y.z-x64[-framework].msi
    Msi
}

/*
 * Fetches the installer for a newer release and proves it is the file the
 * release published, before anything runs it.
 *
 * The proof is the SHA256SUMS.txt that the Release workflow publishes and
 * attests beside the installers. It catches a truncated or corrupted
 * download and a file that is not the one named, which is what can go
 * wrong on the way. It is not a defence against the release itself being
 * replaced by someone who holds the repository: the sums sit in the same
 * place as the files. Someone who wants that check can run
 * "gh attestation verify" on the file, which the documentation says.
 *
 * Nothing here starts the installer. That belongs to the caller, which
 * knows whether it is a person at a window, and which also gets to say
 * so first.
 */
public static class UpdateInstaller
{
    public const string SumsFile = "SHA256SUMS.txt";

    // The largest installer is about 60 MB; this leaves room to grow.
    private const long MaxInstallerBytes = 400L * 1024 * 1024;

    private const int MaxSumsBytes = 64 * 1024;

    // A download that has sent nothing for this long is not coming.
    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(60);

    /*
     * The name the Release workflow gives an installer. The control panel
     * knows which it was installed from (the Setup program or the MSI)
     * and which runtime it carries, so the update replaces like with
     * like: a self-contained install is not swapped for one that needs a
     * runtime that is not there, and an MSI is not "upgraded" by a Setup
     * program that would register a second copy beside it.
     */
    public static string AssetName(
        ReleaseVersion version,
        bool selfContained,
        InstallerKind kind) =>
        $"ByteBridge-{version}-x64"
        + (selfContained ? "" : "-framework")
        + (kind == InstallerKind.Setup ? "-setup.exe" : ".msi");

    /*
     * The hash a sums file lists for a file name, or null. The format is
     * what Get-FileHash and sha256sum both write: 64 hex digits, spaces
     * (sha256sum adds a "*" for binary mode), then the name. The name is
     * compared whole, so "x-setup.exe" never matches "x-framework-setup.exe".
     */
    public static string? ExpectedHash(string sums, string fileName)
    {
        foreach (var raw in sums.Split('\n'))
        {
            var line = raw.Trim();

            if (line.Length < 66 || line[64] != ' ')
            {
                continue;
            }

            var hash = line[..64];
            var name = line[65..].TrimStart(' ', '*');

            if (name == fileName && hash.All(char.IsAsciiHexDigit))
            {
                return hash.ToLowerInvariant();
            }
        }

        return null;
    }

    /*
     * Downloads assetName from the release into folder and returns the
     * path, only once its SHA-256 matches the published sums. On any
     * mismatch the file is deleted and nothing is returned.
     *
     * folder should be one only an administrator can write to. The file
     * is verified here and run later, and in a folder any signed-in user
     * could write, a process of theirs could swap it in between and have
     * it run with administrator rights.
     */
    public static async Task<string> DownloadAsync(
        HttpClient http,
        UpdateInfo release,
        string assetName,
        string folder,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var installer = release.Assets.FirstOrDefault(a => a.Name == assetName)
            ?? throw new UpdateException(
                $"Release {release.Version} has no file named {assetName}.");

        var sumsAsset = release.Assets.FirstOrDefault(a => a.Name == SumsFile)
            ?? throw new UpdateException(
                $"Release {release.Version} has no {SumsFile}, so the "
                + "download could not be checked.");

        if (installer.Size > MaxInstallerBytes)
        {
            throw new UpdateException("The installer is larger than expected.");
        }

        var sums = await FetchSumsAsync(http, sumsAsset, cancellationToken);

        var expected = ExpectedHash(sums, assetName)
            ?? throw new UpdateException(
                $"{SumsFile} does not list {assetName}.");

        Directory.CreateDirectory(folder);
        Clear(folder);

        var target = Path.Combine(folder, assetName);
        var partial = target + ".part";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, installer.Url);

            request.Headers.UserAgent.ParseAdd("ByteBridge");

            using var response = await http.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new UpdateException(
                    $"GitHub answered {(int)response.StatusCode} for the installer.");
            }

            var total = response.Content.Headers.ContentLength
                ?? (installer.Size > 0 ? installer.Size : 0);

            string actual;

            await using (var source =
                await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var file = new FileStream(
                partial,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None))
            using (var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[81920];
                long written = 0;

                using var idle =
                    CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

                while (true)
                {
                    int read;

                    try
                    {
                        idle.CancelAfter(StallTimeout);

                        read = await source.ReadAsync(buffer, idle.Token);
                    }
                    catch (OperationCanceledException)
                        when (!cancellationToken.IsCancellationRequested)
                    {
                        throw new UpdateException(
                            "The download stopped arriving, so it was abandoned.");
                    }

                    if (read == 0)
                    {
                        break;
                    }

                    written += read;

                    if (written > MaxInstallerBytes
                        || (installer.Size > 0 && written > installer.Size))
                    {
                        throw new UpdateException(
                            "The download is larger than the release says.");
                    }

                    hasher.AppendData(buffer, 0, read);
                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);

                    if (total > 0)
                    {
                        progress?.Report(Math.Min(1.0, (double)written / total));
                    }
                }

                actual = Convert.ToHexString(hasher.GetHashAndReset())
                    .ToLower(CultureInfo.InvariantCulture);
            }

            if (actual != expected)
            {
                throw new UpdateException(
                    "The download does not match its published checksum, so "
                    + "it was discarded.");
            }

            File.Move(partial, target, overwrite: true);

            progress?.Report(1.0);

            return target;
        }
        catch
        {
            TryDelete(partial);
            TryDelete(target);
            throw;
        }
    }

    private static async Task<string> FetchSumsAsync(
        HttpClient http,
        UpdateAsset sums,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, sums.Url);

        request.Headers.UserAgent.ParseAdd("ByteBridge");

        using var response = await http.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new UpdateException(
                $"GitHub answered {(int)response.StatusCode} for {SumsFile}.");
        }

        var bytes = await UpdateChecker.ReadCappedAsync(
            response.Content,
            MaxSumsBytes,
            cancellationToken);

        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    // The folder is ours alone, so what is in it is a past download.
    private static void Clear(string folder)
    {
        foreach (var file in Directory.EnumerateFiles(folder))
        {
            TryDelete(file);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
