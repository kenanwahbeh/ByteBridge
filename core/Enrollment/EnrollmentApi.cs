using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ByteBridge.Enrollment;

public sealed record EnrollRequest(
    string DeviceKey,
    string Name,
    string ContactEmail,
    string ClaimHash,
    IReadOnlyDictionary<string, string> Fingerprint);

public enum EnrollOutcome
{
    /* A new request was recorded and the approver has been told. */
    Requested,

    /* This device already has a live request; nothing was duplicated. */
    AlreadyPending,

    /* The device was decided earlier. DecidedStatus says how. */
    Decided
}

public sealed record EnrollResult(
    EnrollOutcome Outcome,
    string? DecidedStatus = null);

public enum ClaimStatus
{
    Pending,
    Approved,
    Rejected,
    Revoked
}

/*
 * AccessTeamDomain and AccessAudience describe the Cloudflare Access
 * application the control plane put in front of this tunnel. Both are
 * null from a control plane that predates them, in which case the
 * gateway is simply not asked to check the token itself.
 */
public sealed record ClaimResult(
    ClaimStatus Status,
    string? Hostname = null,
    string? TunnelToken = null,
    string? AccessTeamDomain = null,
    string? AccessAudience = null);

public interface IEnrollmentApi
{
    Task<EnrollResult> EnrollAsync(
        string server,
        EnrollRequest request,
        CancellationToken cancellationToken);

    Task<ClaimResult> ClaimAsync(
        string server,
        string deviceKey,
        string claimSecret,
        CancellationToken cancellationToken);

    /*
     * Hands this gateway's API key to ByteBalance, so the storefront can
     * call it. Proven with the same claim secret as the token, and only
     * accepted for a device that has an active tunnel.
     */
    Task UploadKeyAsync(
        string server,
        string deviceKey,
        string claimSecret,
        string apiKey,
        CancellationToken cancellationToken);
}

/*
 * The open endpoints of the ByteBalance control plane and nothing else.
 * The status codes are part of its contract and are read here one by one
 * rather than treated as "success or not", because 202, 200 and 409 all
 * mean different things on the enrolment endpoint.
 */
public sealed class HttpEnrollmentApi : IEnrollmentApi
{
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;

    public HttpEnrollmentApi(HttpClient http)
    {
        _http = http;
    }

    public async Task<EnrollResult> EnrollAsync(
        string server,
        EnrollRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            server,
            "api/enroll",
            new
            {
                deviceKey = request.DeviceKey,
                name = request.Name,
                contactEmail = request.ContactEmail,
                claimHash = request.ClaimHash,
                fingerprint = request.Fingerprint
            },
            cancellationToken);

        var body = await ReadAsync(response, cancellationToken);

        switch (response.StatusCode)
        {
            case HttpStatusCode.Accepted:
                return new EnrollResult(EnrollOutcome.Requested);

            case HttpStatusCode.OK:
                return new EnrollResult(EnrollOutcome.AlreadyPending);

            case HttpStatusCode.Conflict:
                return new EnrollResult(
                    EnrollOutcome.Decided,
                    body.Status ?? "unknown");

            case HttpStatusCode.BadRequest:
                throw new EnrollmentException(
                    "The server rejected the request: "
                    + (body.Error ?? "invalid input."));

            default:
                throw Unexpected(response, body);
        }
    }

    public async Task<ClaimResult> ClaimAsync(
        string server,
        string deviceKey,
        string claimSecret,
        CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            server,
            "api/enroll/claim",
            new { deviceKey, claimSecret },
            cancellationToken);

        var body = await ReadAsync(response, cancellationToken);

        switch (response.StatusCode)
        {
            case HttpStatusCode.OK:
                if (string.IsNullOrWhiteSpace(body.Hostname)
                    || string.IsNullOrWhiteSpace(body.TunnelToken))
                {
                    throw new EnrollmentException(
                        "The server approved this device but did not send a tunnel.");
                }

                return new ClaimResult(
                    ClaimStatus.Approved,
                    body.Hostname,
                    body.TunnelToken,
                    Blank(body.AccessTeamDomain),
                    Blank(body.AccessAud));

            /*
             * 202 is the answer for every device that is not approved,
             * refusals included, so the status in the body is what
             * separates "keep waiting" from "stop".
             */
            case HttpStatusCode.Accepted:
                return new ClaimResult(body.Status switch
                {
                    "rejected" => ClaimStatus.Rejected,
                    "revoked" => ClaimStatus.Revoked,
                    _ => ClaimStatus.Pending
                });

            case HttpStatusCode.NotFound:
                throw NotRecognised();

            case HttpStatusCode.Conflict:
                throw new EnrollmentException(
                    "The device is approved but has no active tunnel: "
                    + (body.Error ?? "it was revoked.")
                    + " Ask the ByteBalance administrator.");

            default:
                throw Unexpected(response, body);
        }
    }

    public async Task UploadKeyAsync(
        string server,
        string deviceKey,
        string claimSecret,
        string apiKey,
        CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            server,
            "api/enroll/key",
            new { deviceKey, claimSecret, apiKey },
            cancellationToken);

        var body = await ReadAsync(response, cancellationToken);

        switch (response.StatusCode)
        {
            case HttpStatusCode.OK:
                return;

            case HttpStatusCode.NotFound:
                throw NotRecognised();

            case HttpStatusCode.Conflict:
                throw new EnrollmentException(
                    "ByteBalance will not take the key yet: "
                    + (body.Error ?? "this device has no active tunnel."));

            case HttpStatusCode.BadRequest:
                throw new EnrollmentException(
                    "The server rejected the key: "
                    + (body.Error ?? "invalid input."));

            default:
                throw Unexpected(response, body);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(
        string server,
        string path,
        object payload,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _http.PostAsJsonAsync(
                server.TrimEnd('/') + "/" + path,
                payload,
                Json,
                cancellationToken);
        }
        catch (HttpRequestException error)
        {
            throw new EnrollmentException(
                $"Could not reach the server: {error.Message}",
                transient: true);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new EnrollmentException(
                "The server did not answer in time.",
                transient: true);
        }
    }

    private static async Task<Body> ReadAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<Body>(
                       Json,
                       cancellationToken)
                   ?? new Body();
        }
        catch (JsonException)
        {
            /*
             * A proxy's HTML error page is not worth failing on when the
             * status code already says enough.
             */
            return new Body();
        }
    }

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static EnrollmentException NotRecognised() =>
        new(
            "The server does not recognise this installation. "
            + "Either the enrolment settings were lost or the device was "
            + "removed; ask the ByteBalance administrator to delete the "
            + "device, then enrol again.");

    private static EnrollmentException Unexpected(
        HttpResponseMessage response,
        Body body)
    {
        var code = (int)response.StatusCode;

        return new EnrollmentException(
            $"The server answered {code}"
            + (body.Error != null ? $": {body.Error}" : ".")
            + (code >= 500 ? " Try again shortly." : string.Empty),
            transient: code >= 500);
    }

    private sealed class Body
    {
        public string? Status { get; set; }
        public string? Error { get; set; }
        public string? Hostname { get; set; }
        public string? TunnelToken { get; set; }
        public string? AccessTeamDomain { get; set; }
        public string? AccessAud { get; set; }
    }
}
