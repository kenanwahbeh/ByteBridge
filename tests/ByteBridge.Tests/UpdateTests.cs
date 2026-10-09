using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;
using ByteBridge.Admin;
using ByteBridge.Data;
using ByteBridge.Enrollment;
using ByteBridge.Updates;

namespace ByteBridge.Tests;

/*
 * Updating, as far as it can be tested without GitHub: reading a release,
 * deciding what is newer, remembering the answer, checking a download
 * against its published sums, and the command that reports all of it.
 * The window that offers the installer is Windows-only and not here; it
 * holds no decision these do not.
 */
public class ReleaseVersionTests
{
    [Theory]
    [InlineData("3.2.0", 3, 2, 0, "")]
    [InlineData("v3.2.0", 3, 2, 0, "")]
    [InlineData("V10.20.30", 10, 20, 30, "")]
    [InlineData("3.3.0-beta.1", 3, 3, 0, "beta.1")]
    [InlineData("3.2.0+abc123", 3, 2, 0, "")]
    [InlineData("3.3.0-rc.1+build.5", 3, 3, 0, "rc.1")]
    public void Parses_what_a_release_tag_can_look_like(
        string text, int major, int minor, int patch, string pre)
    {
        Assert.True(ReleaseVersion.TryParse(text, out var version));
        Assert.Equal(new ReleaseVersion(major, minor, patch, pre), version);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("latest")]
    [InlineData("3.2")]
    [InlineData("3.2.0.1")]
    [InlineData("3.02.0")]
    [InlineData("3.-2.0")]
    [InlineData("+3.2.0")]
    [InlineData("3.2.x")]
    [InlineData("3.2.0-")]
    [InlineData("3.2.0-be ta")]
    [InlineData("3.2.0-a..b")]
    [InlineData("1234567890.0.0")]
    public void Refuses_what_is_not_a_version(string text)
    {
        Assert.False(ReleaseVersion.TryParse(text, out _));
    }

    [Fact]
    public void Null_is_not_a_version()
    {
        Assert.False(ReleaseVersion.TryParse(null, out _));
    }

    [Theory]
    [InlineData("3.3.0", "3.2.0")]
    [InlineData("3.2.1", "3.2.0")]
    [InlineData("4.0.0", "3.99.99")]
    [InlineData("3.10.0", "3.9.0")]
    // A release is newer than the pre-release leading up to it.
    [InlineData("3.3.0", "3.3.0-beta.1")]
    [InlineData("3.3.0-beta.2", "3.3.0-beta.1")]
    [InlineData("3.3.0-beta.10", "3.3.0-beta.9")]
    [InlineData("3.3.0-beta", "3.3.0-alpha")]
    [InlineData("3.3.0-beta.1", "3.3.0-beta")]
    // Numbers sort before words.
    [InlineData("3.3.0-beta", "3.3.0-1")]
    public void Orders_by_semantic_precedence(string newer, string older)
    {
        Assert.True(ReleaseVersion.TryParse(newer, out var a));
        Assert.True(ReleaseVersion.TryParse(older, out var b));

        Assert.True(a > b);
        Assert.True(b < a);
        Assert.False(a <= b);
        Assert.NotEqual(0, a.CompareTo(b));
    }

    [Fact]
    public void Equal_versions_compare_equal_whatever_the_build_metadata()
    {
        Assert.True(ReleaseVersion.TryParse("3.2.0+aaa", out var a));
        Assert.True(ReleaseVersion.TryParse("v3.2.0", out var b));

        Assert.Equal(0, a.CompareTo(b));
        Assert.True(a >= b && a <= b);
    }

    [Fact]
    public void Prints_the_way_a_tag_is_written_without_the_v()
    {
        Assert.Equal("3.2.0", new ReleaseVersion(3, 2, 0).ToString());
        Assert.Equal("3.3.0-rc.1", new ReleaseVersion(3, 3, 0, "rc.1").ToString());
    }

    [Fact]
    public void The_running_build_has_a_version()
    {
        // Whatever the test build is stamped with, it must read.
        Assert.True(UpdateService.CurrentVersion().Major >= 0);
    }
}

public class UpdateCheckerTests
{
    private const string Base =
        "https://github.com/kenanwahbeh/ByteBridge/releases/download/v3.3.0/";

    internal static string Release(
        string tag = "v3.3.0",
        bool draft = false,
        bool prerelease = false,
        string? page = "https://github.com/kenanwahbeh/ByteBridge/releases/tag/v3.3.0",
        string body = "Notes.",
        params (string Name, string Url, long Size)[] assets) =>
        JsonSerializer.Serialize(new
        {
            tag_name = tag,
            draft,
            prerelease,
            html_url = page,
            body,
            assets = assets.Select(a => new
            {
                name = a.Name,
                browser_download_url = a.Url,
                size = a.Size
            })
        });

    private static UpdateChecker Checker(EnrollmentFakes.Handler handler) =>
        new(new HttpClient(handler), new ReleaseVersion(3, 2, 0));

    [Fact]
    public async Task Reads_the_newest_release()
    {
        var handler = EnrollmentFakes.Handler.Json(
            HttpStatusCode.OK,
            Release(
                body: "What changed.",
                assets: [("a.exe", Base + "a.exe", 10)]));

        var info = await Checker(handler).GetLatestAsync();

        Assert.Equal(new ReleaseVersion(3, 3, 0), info.Version);
        Assert.Equal("What changed.", info.Notes);
        Assert.Equal(
            "https://github.com/kenanwahbeh/ByteBridge/releases/tag/v3.3.0",
            info.PageUrl);

        var asset = Assert.Single(info.Assets);
        Assert.Equal(new UpdateAsset("a.exe", Base + "a.exe", 10), asset);
    }

    /*
     * A real answer from api.github.com, recorded for the 3.2.0 release
     * and trimmed. The tests above write the JSON they expect; this is
     * the one that is not ours, and the guard against the format moving.
     */
    [Fact]
    public async Task Reads_a_response_recorded_from_github()
    {
        var recorded = await File.ReadAllTextAsync(
            Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "github-latest-release.json"));

        var info = await Checker(
            EnrollmentFakes.Handler.Json(HttpStatusCode.OK, recorded))
            .GetLatestAsync();

        Assert.Equal(new ReleaseVersion(3, 2, 0), info.Version);
        Assert.StartsWith("https://github.com/kenanwahbeh/ByteBridge/", info.PageUrl);
        Assert.NotEmpty(info.Notes);

        // The files the Windows update looks for, and the sums that check them.
        Assert.Contains(info.Assets, a => a.Name == "ByteBridge-3.2.0-x64-setup.exe");
        Assert.Contains(info.Assets, a => a.Name == "ByteBridge-3.2.0-x64-framework.msi");
        Assert.Contains(info.Assets, a => a.Name == UpdateInstaller.SumsFile);
        Assert.All(info.Assets, a => Assert.True(a.Size > 0));

        foreach (var selfContained in new[] { true, false })
        {
            foreach (var kind in Enum.GetValues<InstallerKind>())
            {
                var name = UpdateInstaller.AssetName(info.Version, selfContained, kind);

                Assert.Contains(info.Assets, a => a.Name == name);
            }
        }
    }

    [Fact]
    public async Task Asks_the_latest_endpoint_and_says_which_version_is_asking()
    {
        HttpRequestMessage? seen = null;

        var handler = new EnrollmentFakes.Handler(request =>
        {
            seen = request;

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Release())
            };
        });

        await Checker(handler).GetLatestAsync();

        Assert.Equal(HttpMethod.Get, seen!.Method);
        Assert.Equal(
            "https://api.github.com/repos/kenanwahbeh/ByteBridge/releases/latest",
            seen.RequestUri!.ToString());
        Assert.Equal("ByteBridge/3.2.0", seen.Headers.UserAgent.ToString());

        // Nothing identifying goes with it: no credentials, no cookies.
        Assert.Null(seen.Headers.Authorization);
        Assert.False(seen.Headers.Contains("Cookie"));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Does_not_offer_a_draft_or_a_prerelease(bool draft, bool pre)
    {
        var handler = EnrollmentFakes.Handler.Json(
            HttpStatusCode.OK,
            Release(draft: draft, prerelease: pre));

        await Assert.ThrowsAsync<UpdateException>(
            () => Checker(handler).GetLatestAsync());
    }

    [Theory]
    [InlineData("latest")]
    [InlineData("v3.3")]
    [InlineData("v3.3.0-beta.1")]
    [InlineData("")]
    public async Task Does_not_offer_a_tag_it_cannot_read(string tag)
    {
        var handler = EnrollmentFakes.Handler.Json(
            HttpStatusCode.OK,
            Release(tag: tag));

        await Assert.ThrowsAsync<UpdateException>(
            () => Checker(handler).GetLatestAsync());
    }

    [Fact]
    public async Task Replaces_a_page_link_that_leaves_the_repository()
    {
        var handler = EnrollmentFakes.Handler.Json(
            HttpStatusCode.OK,
            Release(page: "https://evil.example/ByteBridge"));

        var info = await Checker(handler).GetLatestAsync();

        Assert.Equal(
            "https://github.com/kenanwahbeh/ByteBridge/releases/tag/v3.3.0",
            info.PageUrl);
    }

    [Fact]
    public async Task Drops_assets_that_do_not_come_from_this_repositorys_releases()
    {
        var handler = EnrollmentFakes.Handler.Json(
            HttpStatusCode.OK,
            Release(assets:
            [
                ("good.exe", Base + "good.exe", 1),
                ("elsewhere.exe", "https://evil.example/elsewhere.exe", 1),
                ("other-repo.exe",
                    "https://github.com/someone/else/releases/download/v1/other-repo.exe", 1),
                ("http.exe",
                    "http://github.com/kenanwahbeh/ByteBridge/releases/download/v3.3.0/http.exe", 1),
                ("../escape.exe", Base + "escape.exe", 1),
                ("a\\b.exe", Base + "b.exe", 1)
            ]));

        var info = await Checker(handler).GetLatestAsync();

        Assert.Equal(new[] { "good.exe" }, info.Assets.Select(a => a.Name));
    }

    [Fact]
    public async Task Cuts_very_long_release_notes()
    {
        var handler = EnrollmentFakes.Handler.Json(
            HttpStatusCode.OK,
            Release(body: new string('x', 50_000)));

        var info = await Checker(handler).GetLatestAsync();

        Assert.True(info.Notes.Length < 4100);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("\"text\"")]
    [InlineData("{}")]
    public async Task Refuses_an_answer_it_cannot_make_sense_of(string body)
    {
        var handler = EnrollmentFakes.Handler.Json(HttpStatusCode.OK, body);

        await Assert.ThrowsAsync<UpdateException>(
            () => Checker(handler).GetLatestAsync());
    }

    [Fact]
    public async Task Refuses_an_answer_that_never_ends()
    {
        var handler = EnrollmentFakes.Handler.Json(
            HttpStatusCode.OK,
            Release(body: new string('x', 2 * 1024 * 1024)));

        var error = await Assert.ThrowsAsync<UpdateException>(
            () => Checker(handler).GetLatestAsync());

        Assert.Contains("too large", error.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "limiting")]
    [InlineData(HttpStatusCode.TooManyRequests, "limiting")]
    [InlineData(HttpStatusCode.NotFound, "no published release")]
    [InlineData(HttpStatusCode.InternalServerError, "500")]
    public async Task Says_what_went_wrong_in_words(
        HttpStatusCode status, string expected)
    {
        var handler = EnrollmentFakes.Handler.Json(status, "{}");

        var error = await Assert.ThrowsAsync<UpdateException>(
            () => Checker(handler).GetLatestAsync());

        Assert.Contains(expected, error.Message);
    }

    [Fact]
    public async Task Turns_a_network_failure_into_an_update_failure()
    {
        var handler = new EnrollmentFakes.Handler(
            _ => throw new HttpRequestException("no route to host"));

        var error = await Assert.ThrowsAsync<UpdateException>(
            () => Checker(handler).GetLatestAsync());

        Assert.Contains("no route to host", error.Message);
    }

    [Fact]
    public async Task A_caller_who_cancels_is_not_told_github_was_slow()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var handler = EnrollmentFakes.Handler.Json(HttpStatusCode.OK, Release());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Checker(handler).GetLatestAsync(cancelled.Token));
    }
}

public class UpdateServiceTests
{
    private sealed class MovableClock : IClock
    {
        public DateTimeOffset Now { get; set; } =
            new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

        public Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class Rig : IDisposable
    {
        private readonly TempDataRoot _root = new();

        public SqliteDatabase Database { get; }

        public MovableClock Clock { get; } = new();

        public EnrollmentFakes.Handler Handler { get; }

        public UpdateService Service { get; }

        public string Reply { get; set; } = UpdateCheckerTests.Release();

        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        // Runs while a request is "on the wire", to play another process.
        public Action? DuringRequest { get; set; }

        public Rig(ReleaseVersion? current = null)
        {
            Database = _root.OpenDatabase();

            Handler = new EnrollmentFakes.Handler(_ =>
            {
                DuringRequest?.Invoke();

                return new HttpResponseMessage(Status)
                {
                    Content = new StringContent(Reply)
                };
            });

            var version = current ?? new ReleaseVersion(3, 2, 0);

            Service = new UpdateService(
                Database,
                new UpdateChecker(new HttpClient(Handler), version),
                Clock,
                version);
        }

        public int Calls => Handler.Requests.Count;

        public void Dispose() => _root.Dispose();
    }

    [Fact]
    public async Task A_check_finishing_late_does_not_overwrite_a_newer_answer()
    {
        using var rig = new Rig();

        // While this check waits on GitHub, another process records 3.4.0.
        rig.DuringRequest = () =>
        {
            rig.Clock.Now = rig.Clock.Now.AddMinutes(1);

            rig.Database.SetSetting(UpdateService.LatestKey, "3.4.0");
            rig.Database.SetSetting(
                UpdateService.CheckedAtKey,
                rig.Clock.Now.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        };

        var status = await rig.Service.CheckAsync(force: true);

        Assert.Equal(new ReleaseVersion(3, 4, 0), status.Latest);
        Assert.Equal(
            new ReleaseVersion(3, 4, 0),
            rig.Service.Cached().Latest);
    }

    [Fact]
    public void Knows_nothing_before_the_first_check()
    {
        using var rig = new Rig();

        var status = rig.Service.Cached();

        Assert.Null(status.Latest);
        Assert.Null(status.CheckedAt);
        Assert.False(status.Available);
    }

    [Fact]
    public async Task A_newer_release_is_available_and_remembered()
    {
        using var rig = new Rig();

        var status = await rig.Service.CheckAsync(force: false);

        Assert.True(status.Available);
        Assert.Equal(new ReleaseVersion(3, 3, 0), status.Latest);
        Assert.Equal(rig.Clock.Now, status.CheckedAt);
        Assert.Equal(
            "https://github.com/kenanwahbeh/ByteBridge/releases/tag/v3.3.0",
            status.Url);

        // Another process reading the same settings file sees it too.
        var other = UpdateService.Read(rig.Database, new ReleaseVersion(3, 2, 0));

        Assert.True(other.Available);
        Assert.Equal(new ReleaseVersion(3, 3, 0), other.Latest);
    }

    [Fact]
    public async Task The_same_version_is_not_an_update()
    {
        using var rig = new Rig(current: new ReleaseVersion(3, 3, 0));

        var status = await rig.Service.CheckAsync(force: false);

        Assert.False(status.Available);
        Assert.Equal(new ReleaseVersion(3, 3, 0), status.Latest);
    }

    [Fact]
    public async Task A_newer_build_than_the_latest_release_is_not_an_update()
    {
        using var rig = new Rig(current: new ReleaseVersion(4, 0, 0));

        var status = await rig.Service.CheckAsync(force: false);

        Assert.False(status.Available);
    }

    [Fact]
    public async Task Does_not_ask_again_within_a_day()
    {
        using var rig = new Rig();

        await rig.Service.CheckAsync(force: false);

        rig.Clock.Now += TimeSpan.FromHours(23);

        await rig.Service.CheckAsync(force: false);

        Assert.Equal(1, rig.Calls);
    }

    [Fact]
    public async Task Asks_again_after_a_day()
    {
        using var rig = new Rig();

        await rig.Service.CheckAsync(force: false);

        rig.Clock.Now += TimeSpan.FromHours(24);

        await rig.Service.CheckAsync(force: false);

        Assert.Equal(2, rig.Calls);
    }

    [Fact]
    public async Task A_remembered_time_in_the_future_does_not_silence_checks()
    {
        using var rig = new Rig();

        await rig.Service.CheckAsync(force: false);

        // The clock was set back, or the file came from another machine.
        rig.Clock.Now -= TimeSpan.FromDays(30);

        await rig.Service.CheckAsync(force: false);

        Assert.Equal(2, rig.Calls);
    }

    [Fact]
    public async Task Asking_for_it_ignores_the_interval()
    {
        using var rig = new Rig();

        await rig.Service.CheckAsync(force: false);
        await rig.Service.CheckAsync(force: true);

        Assert.Equal(2, rig.Calls);
    }

    [Fact]
    public async Task Does_nothing_by_itself_when_checks_are_off()
    {
        using var rig = new Rig();

        UpdateService.SetEnabled(rig.Database, false);

        var status = await rig.Service.CheckAsync(force: false);

        Assert.Equal(0, rig.Calls);
        Assert.Null(status.Latest);
        Assert.False(rig.Service.Enabled);
    }

    [Fact]
    public async Task Checks_when_asked_even_with_checks_off()
    {
        using var rig = new Rig();

        UpdateService.SetEnabled(rig.Database, false);

        var status = await rig.Service.CheckAsync(force: true);

        Assert.Equal(1, rig.Calls);
        Assert.True(status.Available);
    }

    [Fact]
    public void Checks_are_on_until_someone_turns_them_off()
    {
        using var rig = new Rig();

        Assert.True(UpdateService.IsEnabled(rig.Database));

        UpdateService.SetEnabled(rig.Database, false);
        Assert.False(UpdateService.IsEnabled(rig.Database));

        UpdateService.SetEnabled(rig.Database, true);
        Assert.True(UpdateService.IsEnabled(rig.Database));
    }

    [Fact]
    public async Task A_failed_check_keeps_the_last_answer_and_says_why()
    {
        using var rig = new Rig();

        await rig.Service.CheckAsync(force: false);

        rig.Status = HttpStatusCode.Forbidden;
        rig.Clock.Now += TimeSpan.FromDays(2);

        var status = await rig.Service.CheckAsync(force: false);

        Assert.True(status.Available);
        Assert.NotNull(status.Error);

        // A failure is not a check: it is tried again next time round.
        rig.Status = HttpStatusCode.OK;

        var again = await rig.Service.CheckAsync(force: false);

        Assert.Null(again.Error);
        Assert.Equal(3, rig.Calls);
    }

    [Fact]
    public async Task Ignores_a_remembered_link_that_leaves_the_repository()
    {
        using var rig = new Rig();

        await rig.Service.CheckAsync(force: false);

        // The settings file is writable by an administrator only, but the
        // link is opened in a browser, so it is checked on the way out too.
        rig.Database.SetSetting("Updates.Url", "https://evil.example/");

        Assert.Null(rig.Service.Cached().Url);
    }

    [Fact]
    public void Ignores_a_remembered_version_that_is_garbage()
    {
        using var rig = new Rig();

        rig.Database.SetSetting("Updates.Latest", "banana");
        rig.Database.SetSetting("Updates.CheckedAt", "yesterday-ish");

        var status = rig.Service.Cached();

        Assert.Null(status.Latest);
        Assert.Null(status.CheckedAt);
        Assert.False(status.Available);
    }

    [Fact]
    public async Task Keeps_the_release_it_read_for_the_installer()
    {
        using var rig = new Rig();

        Assert.Null(rig.Service.LastInfo);

        await rig.Service.CheckAsync(force: false);

        Assert.Equal(new ReleaseVersion(3, 3, 0), rig.Service.LastInfo!.Version);
    }
}

public class UpdateInstallerTests : IDisposable
{
    private const string Base =
        "https://github.com/kenanwahbeh/ByteBridge/releases/download/v3.3.0/";

    private const string Setup = "ByteBridge-3.3.0-x64-setup.exe";

    private static readonly byte[] Payload =
        Encoding.ASCII.GetBytes("pretend this is an installer");

    private readonly string _folder = Path.Combine(
        Path.GetTempPath(),
        "bytebridge-tests",
        Guid.NewGuid().ToString("n"),
        "updates");

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path.GetDirectoryName(_folder)!, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static string Sha(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static UpdateInfo Info(
        string[]? names = null,
        long? size = null) =>
        new(
            new ReleaseVersion(3, 3, 0),
            "https://github.com/kenanwahbeh/ByteBridge/releases/tag/v3.3.0",
            "",
            (names ?? [Setup, UpdateInstaller.SumsFile])
                .Select(n => new UpdateAsset(
                    n,
                    Base + n,
                    n == Setup ? size ?? Payload.Length : 100))
                .ToList());

    private static EnrollmentFakes.Handler Server(
        string sums,
        byte[]? installer = null) =>
        new(request =>
        {
            var name = request.RequestUri!.Segments[^1];

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = name == UpdateInstaller.SumsFile
                    ? new StringContent(sums)
                    : new ByteArrayContent(installer ?? Payload)
            };
        });

    [Theory]
    [InlineData(true, InstallerKind.Setup, "ByteBridge-3.3.0-x64-setup.exe")]
    [InlineData(true, InstallerKind.Msi, "ByteBridge-3.3.0-x64.msi")]
    [InlineData(false, InstallerKind.Setup, "ByteBridge-3.3.0-x64-framework-setup.exe")]
    [InlineData(false, InstallerKind.Msi, "ByteBridge-3.3.0-x64-framework.msi")]
    public void Names_the_installer_the_release_workflow_publishes(
        bool selfContained, InstallerKind kind, string expected)
    {
        Assert.Equal(
            expected,
            UpdateInstaller.AssetName(new ReleaseVersion(3, 3, 0), selfContained, kind));
    }

    [Fact]
    public void Reads_a_hash_from_either_sums_format()
    {
        var a = new string('a', 64);
        var b = new string('B', 64);
        var c = new string('c', 64);

        var sums =
            $"{a}  ByteBridge-3.3.0-x64-setup.exe\r\n"
            + $"{b} *ByteBridge-3.3.0-x64.msi\n"
            + $"{c}  ByteBridge-3.3.0-x64-framework-setup.exe\n";

        Assert.Equal(a, UpdateInstaller.ExpectedHash(sums, "ByteBridge-3.3.0-x64-setup.exe"));
        Assert.Equal(
            b.ToLowerInvariant(),
            UpdateInstaller.ExpectedHash(sums, "ByteBridge-3.3.0-x64.msi"));
        Assert.Equal(
            c,
            UpdateInstaller.ExpectedHash(sums, "ByteBridge-3.3.0-x64-framework-setup.exe"));
    }

    [Theory]
    [InlineData("ByteBridge-3.3.0-x64-setup.ex")]
    [InlineData("setup.exe")]
    [InlineData("")]
    public void Matches_the_whole_file_name_and_nothing_shorter(string name)
    {
        var sums = new string('a', 64) + "  ByteBridge-3.3.0-x64-setup.exe\n";

        Assert.Null(UpdateInstaller.ExpectedHash(sums, name));
    }

    [Theory]
    [InlineData("not a hash  ByteBridge-3.3.0-x64-setup.exe")]
    [InlineData("zzzz  ByteBridge-3.3.0-x64-setup.exe")]
    [InlineData("")]
    public void Skips_lines_that_are_not_hashes(string line)
    {
        Assert.Null(UpdateInstaller.ExpectedHash(line, "ByteBridge-3.3.0-x64-setup.exe"));
    }

    [Fact]
    public async Task Downloads_and_keeps_a_file_that_matches_its_sums()
    {
        var handler = Server($"{Sha(Payload)}  {Setup}\n");

        var seen = new List<double>();

        var path = await UpdateInstaller.DownloadAsync(
            new HttpClient(handler),
            Info(),
            Setup,
            _folder,
            new Progress<double>(seen.Add));

        Assert.Equal(Path.Combine(_folder, Setup), path);
        Assert.Equal(Payload, await File.ReadAllBytesAsync(path));
        Assert.False(File.Exists(path + ".part"));
    }

    [Fact]
    public async Task Discards_a_file_that_does_not_match_its_sums()
    {
        var handler = Server($"{new string('0', 64)}  {Setup}\n");

        var error = await Assert.ThrowsAsync<UpdateException>(() =>
            UpdateInstaller.DownloadAsync(
                new HttpClient(handler), Info(), Setup, _folder));

        Assert.Contains("checksum", error.Message);
        Assert.Empty(Directory.GetFiles(_folder));
    }

    [Fact]
    public async Task Discards_a_file_that_is_not_the_size_the_release_lists()
    {
        var handler = Server($"{Sha(Payload)}  {Setup}\n");

        await Assert.ThrowsAsync<UpdateException>(() =>
            UpdateInstaller.DownloadAsync(
                new HttpClient(handler),
                Info(size: Payload.Length - 5),
                Setup,
                _folder));

        Assert.Empty(Directory.GetFiles(_folder));
    }

    [Fact]
    public async Task Refuses_when_the_sums_do_not_list_the_file()
    {
        var handler = Server($"{Sha(Payload)}  some-other-file.exe\n");

        var error = await Assert.ThrowsAsync<UpdateException>(() =>
            UpdateInstaller.DownloadAsync(
                new HttpClient(handler), Info(), Setup, _folder));

        Assert.Contains("does not list", error.Message);
    }

    [Fact]
    public async Task Refuses_when_the_release_has_no_sums_at_all()
    {
        var handler = Server("");

        var error = await Assert.ThrowsAsync<UpdateException>(() =>
            UpdateInstaller.DownloadAsync(
                new HttpClient(handler),
                Info(names: [Setup]),
                Setup,
                _folder));

        Assert.Contains(UpdateInstaller.SumsFile, error.Message);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Refuses_when_the_release_has_no_such_installer()
    {
        var handler = Server("");

        await Assert.ThrowsAsync<UpdateException>(() =>
            UpdateInstaller.DownloadAsync(
                new HttpClient(handler),
                Info(names: [UpdateInstaller.SumsFile]),
                Setup,
                _folder));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Clears_an_earlier_download_first()
    {
        Directory.CreateDirectory(_folder);
        await File.WriteAllTextAsync(Path.Combine(_folder, "old.exe"), "old");

        var handler = Server($"{Sha(Payload)}  {Setup}\n");

        await UpdateInstaller.DownloadAsync(
            new HttpClient(handler), Info(), Setup, _folder);

        Assert.Equal(new[] { Setup }, Directory.GetFiles(_folder).Select(f => Path.GetFileName(f)));
    }

    [Fact]
    public async Task Reports_progress_up_to_done()
    {
        var handler = Server($"{Sha(Payload)}  {Setup}\n");

        var seen = new List<double>();

        await UpdateInstaller.DownloadAsync(
            new HttpClient(handler),
            Info(),
            Setup,
            _folder,
            new SyncProgress(seen));

        Assert.Equal(1.0, seen[^1]);
        Assert.All(seen, p => Assert.InRange(p, 0.0, 1.0));
    }

    [Fact]
    public async Task A_cancelled_download_leaves_nothing_behind()
    {
        var handler = Server($"{Sha(Payload)}  {Setup}\n");

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            UpdateInstaller.DownloadAsync(
                new HttpClient(handler),
                Info(),
                Setup,
                _folder,
                null,
                cancelled.Token));

        if (Directory.Exists(_folder))
        {
            Assert.Empty(Directory.GetFiles(_folder));
        }
    }

    private sealed class SyncProgress(List<double> seen) : IProgress<double>
    {
        public void Report(double value) => seen.Add(value);
    }
}

[Collection("Console redirection")]
public class UpdateCommandTests
{
    private sealed class Rig : IDisposable
    {
        private readonly TempDataRoot _root = new();

        public SqliteDatabase Database { get; }

        public UpdateService Service { get; }

        public StringWriter Out { get; } = new();

        public StringWriter Error { get; } = new();

        public Rig(
            string? latest = "v3.3.0",
            HttpStatusCode status = HttpStatusCode.OK)
        {
            Database = _root.OpenDatabase();

            var handler = new EnrollmentFakes.Handler(_ =>
                new HttpResponseMessage(status)
                {
                    Content = new StringContent(
                        latest == null ? "{}" : UpdateCheckerTests.Release(tag: latest))
                });

            var current = new ReleaseVersion(3, 2, 0);

            Service = new UpdateService(
                Database,
                new UpdateChecker(new HttpClient(handler), current),
                new SystemClock(),
                current);
        }

        public int Run(params string[] args) =>
            UpdateCommands.Run(args, Database, Service, Out, Error);

        public void Dispose() => _root.Dispose();
    }

    [Fact]
    public void Checking_reports_a_newer_release_and_how_to_get_it()
    {
        using var rig = new Rig();

        Assert.Equal(0, rig.Run("update"));

        var text = rig.Out.ToString();

        Assert.Contains("Installed:  3.2.0", text);
        Assert.Contains("Latest:     3.3.0", text);
        Assert.Contains("A newer version is available: 3.3.0", text);
        Assert.Contains("releases/tag/v3.3.0", text);
        Assert.Contains("To update", text);
        Assert.Equal("", rig.Error.ToString());
    }

    [Fact]
    public void Check_is_the_default_and_also_a_word()
    {
        using var rig = new Rig();

        Assert.Equal(0, rig.Run("update", "check"));
        Assert.Contains("3.3.0", rig.Out.ToString());
    }

    [Fact]
    public void Says_so_when_there_is_nothing_newer()
    {
        using var rig = new Rig(latest: "v3.2.0");

        Assert.Equal(0, rig.Run("update", "check"));

        var text = rig.Out.ToString();

        Assert.Contains("up to date", text);
        Assert.DoesNotContain("A newer version", text);
    }

    [Fact]
    public void A_failed_check_is_an_error_and_exits_nonzero()
    {
        using var rig = new Rig(status: HttpStatusCode.Forbidden);

        Assert.Equal(1, rig.Run("update", "check"));

        Assert.Contains("limiting", rig.Error.ToString());
        Assert.Equal("", rig.Out.ToString());
    }

    [Fact]
    public void Status_never_goes_to_the_network()
    {
        using var rig = new Rig();

        Assert.Equal(0, rig.Run("update", "status"));

        Assert.Contains("not checked yet", rig.Out.ToString());
        Assert.False(rig.Service.Cached().Latest.HasValue);
    }

    [Fact]
    public void Status_repeats_the_last_check()
    {
        using var rig = new Rig();

        rig.Run("update", "check");
        rig.Out.GetStringBuilder().Clear();

        Assert.Equal(0, rig.Run("update", "status"));
        Assert.Contains("A newer version is available: 3.3.0", rig.Out.ToString());
    }

    [Fact]
    public void Off_and_on_flip_the_setting_and_say_what_they_mean()
    {
        using var rig = new Rig();

        Assert.Equal(0, rig.Run("update", "off"));
        Assert.False(UpdateService.IsEnabled(rig.Database));
        Assert.Contains("Nothing is sent", rig.Out.ToString());

        Assert.Equal(0, rig.Run("update", "on"));
        Assert.True(UpdateService.IsEnabled(rig.Database));
        Assert.Contains("never installs", rig.Out.ToString());
    }

    [Fact]
    public void Status_shows_whether_checks_are_on()
    {
        using var rig = new Rig();

        rig.Run("update", "off");
        rig.Out.GetStringBuilder().Clear();

        rig.Run("update", "status");

        Assert.Contains("off", rig.Out.ToString());
    }

    [Theory]
    [InlineData("update", "frobnicate")]
    [InlineData("update", "check", "extra")]
    public void Refuses_what_it_does_not_understand(params string[] args)
    {
        using var rig = new Rig();

        Assert.Equal(1, rig.Run(args));
        Assert.Contains("error:", rig.Error.ToString());
    }

    [Fact]
    public void Update_is_a_command_of_the_service_executable()
    {
        using var rig = new Rig();

        var originalOut = Console.Out;
        var originalError = Console.Error;

        Console.SetOut(rig.Out);
        Console.SetError(rig.Error);

        try
        {
            Assert.Equal(
                0,
                Cli.Run(["update", "check"], rig.Database, null, rig.Service));
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }

        Assert.Contains("A newer version is available", rig.Out.ToString());
        Assert.Contains("update", Cli.Usage);
    }

    [Theory]
    [InlineData(true, false, "control panel")]
    [InlineData(false, true, "apt install --only-upgrade bytebridge")]
    [InlineData(false, false, "install.sh")]
    public void Points_to_the_way_this_install_is_updated(
        bool windows, bool packaged, string expected)
    {
        Assert.Contains(expected, UpdateCommands.HowToUpdate(windows, packaged));
    }

    [Fact]
    public void Never_suggests_installing_by_itself()
    {
        // Nothing on the command line downloads or runs a build.
        Assert.DoesNotContain("download and install automatically", UpdateCommands.Usage);
        Assert.Contains("Look for", UpdateCommands.Usage);
    }
}

public class UpdateStatsTests
{
    [Fact]
    public async Task Stats_reports_the_version_and_the_last_update_answer()
    {
        using var gateway = new GatewayHarness();

        gateway.Database.SetSetting("Updates.Latest", "99.0.0");
        gateway.Database.SetSetting(
            "Updates.CheckedAt",
            "2026-10-09T12:00:00.0000000+00:00");

        var (status, body) = await GatewayHarness.Read(gateway.Get("/stats"));

        Assert.Equal(HttpStatusCode.OK, status);

        var root = JsonDocument.Parse(body).RootElement;

        Assert.False(string.IsNullOrEmpty(root.GetProperty("version").GetString()));

        var update = root.GetProperty("update");

        Assert.True(update.GetProperty("available").GetBoolean());
        Assert.Equal("99.0.0", update.GetProperty("latest").GetString());
    }

    [Fact]
    public async Task Stats_says_no_update_when_nothing_was_ever_checked()
    {
        using var gateway = new GatewayHarness();

        var (_, body) = await GatewayHarness.Read(gateway.Get("/stats"));

        var update = JsonDocument.Parse(body).RootElement.GetProperty("update");

        Assert.False(update.GetProperty("available").GetBoolean());
        Assert.Equal(JsonValueKind.Null, update.GetProperty("latest").ValueKind);
    }

    /*
     * /health answers anyone who can reach the tunnel. A gateway that
     * announced it was out of date there would be telling strangers which
     * ones are worth trying.
     */
    [Fact]
    public async Task Health_says_nothing_about_updates()
    {
        using var gateway = new GatewayHarness();

        gateway.Database.SetSetting("Updates.Latest", "99.0.0");

        var (_, body) = await GatewayHarness.Read(gateway.Get("/health", key: ""));

        Assert.DoesNotContain("update", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("version", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("99.0.0", body);
    }
}
