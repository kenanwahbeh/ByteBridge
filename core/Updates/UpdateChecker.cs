using System.Net;
using System.Text.Json;

namespace ByteBridge.Updates;

public sealed record UpdateAsset(string Name, string Url, long Size);

/*
 * What the newest release says about itself. Only the fields the app
 * uses; everything is checked on the way in, because the body is read
 * from the network and nothing in it is trusted to be well-formed.
 */
public sealed record UpdateInfo(
    ReleaseVersion Version,
    string PageUrl,
    string Notes,
    IReadOnlyList<UpdateAsset> Assets);

public sealed class UpdateException : Exception
{
    public UpdateException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

/*
 * Asks GitHub for the newest published release.
 *
 * "latest" is GitHub's own notion: the newest release that is neither a
 * draft nor a pre-release, so a beta is never offered to someone on a
 * stable version. The request is an anonymous GET; what leaves the
 * machine is the address it comes from and the User-Agent below, which
 * carries the installed version and nothing else.
 *
 * Links in the reply are only accepted when they point back into this
 * repository on github.com. The reply arrives over TLS from GitHub, but
 * what the app does with a link is open it or download an installer
 * from it, and that is not worth leaving to whatever a release body or
 * an asset happens to say.
 */
public sealed class UpdateChecker
{
    public const string Repository = "kenanwahbeh/ByteBridge";

    public const string RepositoryUrl =
        "https://github.com/" + Repository + "/";

    private static readonly Uri LatestUri = new(
        "https://api.github.com/repos/" + Repository + "/releases/latest");

    private const int MaxResponseBytes = 1024 * 1024;

    private const int MaxNotesCharacters = 4000;

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private readonly HttpClient _http;

    private readonly string _agent;

    public UpdateChecker(HttpClient http, ReleaseVersion current)
    {
        _http = http;
        _agent = "ByteBridge/" + current;
    }

    public static HttpClient CreateHttpClient() => new()
    {
        // Each request carries its own, shorter, deadline.
        Timeout = TimeSpan.FromMinutes(10)
    };

    public async Task<UpdateInfo> GetLatestAsync(
        CancellationToken cancellationToken = default)
    {
        using var deadline =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        deadline.CancelAfter(Timeout);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestUri);

            request.Headers.UserAgent.ParseAdd(_agent);
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

            using var response = await _http.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                deadline.Token);

            if (response.StatusCode is HttpStatusCode.Forbidden
                or HttpStatusCode.TooManyRequests)
            {
                throw new UpdateException(
                    "GitHub is limiting requests from this address. "
                    + "Try again later.");
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new UpdateException(
                    "GitHub has no published release to compare with.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new UpdateException(
                    $"GitHub answered {(int)response.StatusCode}.");
            }

            var body = await ReadCappedAsync(
                response.Content,
                MaxResponseBytes,
                deadline.Token);

            return Parse(body);
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            throw new UpdateException("GitHub did not answer in time.");
        }
        catch (HttpRequestException error)
        {
            throw new UpdateException(
                "Could not reach GitHub: " + error.Message,
                error);
        }
    }

    internal static UpdateInfo Parse(byte[] body)
    {
        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException error)
        {
            throw new UpdateException(
                "GitHub's answer was not understood.",
                error);
        }

        using (document)
        {
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new UpdateException("GitHub's answer was not understood.");
            }

            if (Flag(root, "draft") || Flag(root, "prerelease"))
            {
                throw new UpdateException(
                    "GitHub has no published release to compare with.");
            }

            if (!ReleaseVersion.TryParse(Text(root, "tag_name"), out var version)
                || version.IsPreRelease)
            {
                throw new UpdateException(
                    "The newest release has a version this app cannot read.");
            }

            var page = Text(root, "html_url") ?? "";

            if (!page.StartsWith(RepositoryUrl, StringComparison.Ordinal))
            {
                page = RepositoryUrl + "releases/tag/v" + version;
            }

            var notes = Text(root, "body") ?? "";

            if (notes.Length > MaxNotesCharacters)
            {
                notes = notes[..MaxNotesCharacters] + "...";
            }

            var assets = new List<UpdateAsset>();

            if (root.TryGetProperty("assets", out var list)
                && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in list.EnumerateArray())
                {
                    var name = Text(item, "name");
                    var url = Text(item, "browser_download_url");

                    if (name == null
                        || url == null
                        || name.Contains('/')
                        || name.Contains('\\')
                        || !url.StartsWith(
                            RepositoryUrl + "releases/download/",
                            StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var size = item.TryGetProperty("size", out var s)
                        && s.TryGetInt64(out var n)
                        ? n
                        : -1;

                    assets.Add(new UpdateAsset(name, url, size));
                }
            }

            return new UpdateInfo(version, page, notes, assets);
        }
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool Flag(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.True;

    /*
     * Reads at most the cap and fails past it, so a server that keeps
     * sending cannot grow the process; Content-Length is a claim, not a
     * limit.
     */
    internal static async Task<byte[]> ReadCappedAsync(
        HttpContent content,
        int limit,
        CancellationToken cancellationToken)
    {
        await using var stream =
            await content.ReadAsStreamAsync(cancellationToken);

        using var buffer = new MemoryStream();

        var chunk = new byte[16 * 1024];

        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken);

            if (read == 0)
            {
                return buffer.ToArray();
            }

            if (buffer.Length + read > limit)
            {
                throw new UpdateException("GitHub's answer was too large.");
            }

            buffer.Write(chunk, 0, read);
        }
    }
}
