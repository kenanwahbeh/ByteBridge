using System.Net;
using System.Text.Json;
using Xunit;
using ByteBridge.Enrollment;
using static ByteBridge.Tests.EnrollmentFakes;

namespace ByteBridge.Tests;

public class EnrollmentApiTests
{
    private static readonly EnrollRequest Request = new(
        "bb-abc", "Acme", "owner@example.com", new string('a', 64),
        new Dictionary<string, string> { ["hostname"] = "PC1" });

    private static HttpEnrollmentApi Api(Handler handler) =>
        new(new HttpClient(handler));

    [Fact]
    public async Task Enroll_posts_the_fields_the_control_plane_expects()
    {
        var handler = Handler.Json(HttpStatusCode.Accepted, """{"status":"pending"}""");

        await Api(handler).EnrollAsync("https://bytebalancetech.com/", Request, default);

        var sent = handler.Requests.Single();

        Assert.Equal("https://bytebalancetech.com/api/enroll", sent.Uri!.ToString());

        var body = JsonDocument.Parse(sent.Body).RootElement;

        Assert.Equal("bb-abc", body.GetProperty("deviceKey").GetString());
        Assert.Equal("Acme", body.GetProperty("name").GetString());
        Assert.Equal("owner@example.com", body.GetProperty("contactEmail").GetString());
        Assert.Equal(new string('a', 64), body.GetProperty("claimHash").GetString());
        Assert.Equal("PC1", body.GetProperty("fingerprint").GetProperty("hostname").GetString());
        Assert.False(body.TryGetProperty("claimSecret", out _));
        Assert.False(body.TryGetProperty("apiKey", out _));
    }

    [Theory]
    [InlineData(HttpStatusCode.Accepted, """{"status":"pending"}""", EnrollOutcome.Requested)]
    [InlineData(HttpStatusCode.OK, """{"status":"pending","alreadyRequested":true}""", EnrollOutcome.AlreadyPending)]
    public async Task Enroll_reads_the_success_codes(HttpStatusCode code, string json, EnrollOutcome expected)
    {
        var result = await Api(Handler.Json(code, json))
            .EnrollAsync("https://x.test", Request, default);

        Assert.Equal(expected, result.Outcome);
    }

    [Fact]
    public async Task Enroll_409_carries_how_the_device_was_decided()
    {
        var result = await Api(Handler.Json(HttpStatusCode.Conflict, """{"status":"rejected"}"""))
            .EnrollAsync("https://x.test", Request, default);

        Assert.Equal(EnrollOutcome.Decided, result.Outcome);
        Assert.Equal("rejected", result.DecidedStatus);
    }

    [Fact]
    public async Task Enroll_400_shows_the_servers_reason()
    {
        var error = await Assert.ThrowsAsync<EnrollmentException>(() =>
            Api(Handler.Json(HttpStatusCode.BadRequest, """{"error":"contactEmail is required"}"""))
                .EnrollAsync("https://x.test", Request, default));

        Assert.Contains("contactEmail is required", error.Message);
        Assert.False(error.Transient);
    }

    [Fact]
    public async Task A_server_error_is_transient()
    {
        var error = await Assert.ThrowsAsync<EnrollmentException>(() =>
            Api(Handler.Json(HttpStatusCode.BadGateway, """{"error":"Could not retrieve the tunnel token."}"""))
                .ClaimAsync("https://x.test", "bb-abc", "secret", default));

        Assert.True(error.Transient);
    }

    [Fact]
    public async Task A_network_failure_is_transient()
    {
        var api = new HttpEnrollmentApi(new HttpClient(
            new Handler(_ => throw new HttpRequestException("no route"))));

        var error = await Assert.ThrowsAsync<EnrollmentException>(
            () => api.EnrollAsync("https://x.test", Request, default));

        Assert.True(error.Transient);
    }

    [Fact]
    public async Task A_proxy_error_page_is_reported_by_status_not_by_a_parse_crash()
    {
        var handler = new Handler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent("<html>upstream down</html>")
        });

        var error = await Assert.ThrowsAsync<EnrollmentException>(
            () => Api(handler).EnrollAsync("https://x.test", Request, default));

        Assert.Contains("503", error.Message);
        Assert.True(error.Transient);
    }

    [Fact]
    public async Task Claim_200_returns_the_hostname_token_and_access_application()
    {
        var result = await Api(Handler.Json(
                HttpStatusCode.OK,
                """
                {"status":"approved","hostname":"acme.techn0.dpdns.org","tunnelToken":"tok",
                 "accessTeamDomain":"team.cloudflareaccess.com","accessAud":"aud-1"}
                """))
            .ClaimAsync("https://x.test", "bb-abc", "secret", default);

        Assert.Equal(ClaimStatus.Approved, result.Status);
        Assert.Equal("acme.techn0.dpdns.org", result.Hostname);
        Assert.Equal("tok", result.TunnelToken);
        Assert.Equal("team.cloudflareaccess.com", result.AccessTeamDomain);
        Assert.Equal("aud-1", result.AccessAudience);
    }

    [Fact]
    public async Task Claim_200_from_an_older_server_has_no_access_application()
    {
        var result = await Api(Handler.Json(
                HttpStatusCode.OK,
                """{"status":"approved","hostname":"h.techn0.dpdns.org","tunnelToken":"tok"}"""))
            .ClaimAsync("https://x.test", "bb-abc", "secret", default);

        Assert.Null(result.AccessTeamDomain);
        Assert.Null(result.AccessAudience);
    }

    [Fact]
    public async Task Claim_sends_the_secret_not_its_hash()
    {
        var handler = Handler.Json(HttpStatusCode.Accepted, """{"status":"pending"}""");

        await Api(handler).ClaimAsync("https://x.test", "bb-abc", "the-secret", default);

        var body = JsonDocument.Parse(handler.Requests.Single().Body).RootElement;

        Assert.Equal("https://x.test/api/enroll/claim", handler.Requests.Single().Uri!.ToString());
        Assert.Equal("the-secret", body.GetProperty("claimSecret").GetString());
        Assert.Equal("bb-abc", body.GetProperty("deviceKey").GetString());
    }

    [Theory]
    [InlineData("pending", ClaimStatus.Pending)]
    [InlineData("rejected", ClaimStatus.Rejected)]
    [InlineData("revoked", ClaimStatus.Revoked)]
    [InlineData("something-new", ClaimStatus.Pending)]
    public async Task Claim_202_is_told_apart_by_the_status_in_the_body(string status, ClaimStatus expected)
    {
        var result = await Api(Handler.Json(HttpStatusCode.Accepted, $$"""{"status":"{{status}}"}"""))
            .ClaimAsync("https://x.test", "bb-abc", "secret", default);

        Assert.Equal(expected, result.Status);
    }

    [Fact]
    public async Task Claim_200_without_a_token_is_an_error_not_a_success()
    {
        await Assert.ThrowsAsync<EnrollmentException>(() =>
            Api(Handler.Json(HttpStatusCode.OK, """{"status":"approved"}"""))
                .ClaimAsync("https://x.test", "bb-abc", "secret", default));
    }

    [Fact]
    public async Task Claim_404_explains_how_to_recover()
    {
        var error = await Assert.ThrowsAsync<EnrollmentException>(() =>
            Api(Handler.Json(HttpStatusCode.NotFound, """{"error":"Not enrolled."}"""))
                .ClaimAsync("https://x.test", "bb-abc", "secret", default));

        Assert.Contains("administrator", error.Message);
        Assert.False(error.Transient);
    }

    [Fact]
    public async Task Claim_409_means_no_active_tunnel()
    {
        var error = await Assert.ThrowsAsync<EnrollmentException>(() =>
            Api(Handler.Json(HttpStatusCode.Conflict, """{"error":"No active tunnel."}"""))
                .ClaimAsync("https://x.test", "bb-abc", "secret", default));

        Assert.Contains("No active tunnel", error.Message);
    }

    /* ---- the key ---- */

    [Fact]
    public async Task Upload_key_sends_the_secret_and_the_key_to_the_key_endpoint()
    {
        var handler = Handler.Json(HttpStatusCode.OK, """{"status":"stored"}""");

        await Api(handler).UploadKeyAsync("https://x.test/", "bb-abc", "the-secret", "the-key", default);

        var sent = handler.Requests.Single();
        var body = JsonDocument.Parse(sent.Body).RootElement;

        Assert.Equal("https://x.test/api/enroll/key", sent.Uri!.ToString());
        Assert.Equal("bb-abc", body.GetProperty("deviceKey").GetString());
        Assert.Equal("the-secret", body.GetProperty("claimSecret").GetString());
        Assert.Equal("the-key", body.GetProperty("apiKey").GetString());
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "administrator")]
    [InlineData(HttpStatusCode.Conflict, "will not take the key yet")]
    [InlineData(HttpStatusCode.BadRequest, "rejected the key")]
    public async Task Upload_key_failures_say_what_to_do(HttpStatusCode code, string expected)
    {
        var error = await Assert.ThrowsAsync<EnrollmentException>(() =>
            Api(Handler.Json(code, """{"error":"nope"}"""))
                .UploadKeyAsync("https://x.test", "bb-abc", "s", "k", default));

        Assert.Contains(expected, error.Message);
        Assert.False(error.Transient);
    }

    [Fact]
    public async Task Upload_key_server_errors_are_transient()
    {
        var error = await Assert.ThrowsAsync<EnrollmentException>(() =>
            Api(Handler.Json(HttpStatusCode.InternalServerError, "{}"))
                .UploadKeyAsync("https://x.test", "bb-abc", "s", "k", default));

        Assert.True(error.Transient);
    }
}
