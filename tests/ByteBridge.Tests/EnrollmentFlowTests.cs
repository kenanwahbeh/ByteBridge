using Xunit;
using ByteBridge.Enrollment;
using static ByteBridge.Tests.EnrollmentFakes;

namespace ByteBridge.Tests;

public class EnrollmentFlowTests
{
    private const string Token = "eyJ-tunnel-token-not-for-logs";
    private const string ApiKey = "the-gateway-api-key";

    private readonly Api _api = new();
    private readonly MemoryStore _store = new();
    private readonly Connector _connector = new();
    private readonly FakeGateway _gateway = new();
    private readonly Clock _clock = new();
    private readonly StringWriter _out = new();

    private EnrollmentFlow Flow() =>
        new(_api, _store, _connector, _gateway, new Machine(), _clock, _out);

    private static EnrollOptions Enroll(bool wait = true, string email = "owner@example.com") =>
        new("https://bytebalancetech.com", email, "Acme Sales",
            wait, TimeSpan.FromMinutes(30), ReplaceConnector: false);

    private static ClaimResult Approved() =>
        new(ClaimStatus.Approved, "acme-sales.techn0.dpdns.org", Token,
            "team.cloudflareaccess.com", "aud-tag-123");

    private static EnrollmentState Saved(
        Phase phase = Phase.Requested,
        string email = "owner@example.com",
        string server = "https://bytebalancetech.com") =>
        new(server, "bb-1", "the-claim-secret", "Acme", email, phase,
            new DateTimeOffset(2026, 9, 22, 12, 0, 0, TimeSpan.Zero),
            phase == Phase.Connected ? "acme.techn0.dpdns.org" : null);

    [Fact]
    public async Task Waits_for_approval_then_installs_the_connector()
    {
        _api.Enrolls.Enqueue(new EnrollResult(EnrollOutcome.Requested));
        _api.Claims.Enqueue(new ClaimResult(ClaimStatus.Pending));
        _api.Claims.Enqueue(new ClaimResult(ClaimStatus.Pending));
        _api.Claims.Enqueue(Approved());

        var code = await Flow().EnrollAsync(Enroll(), default);

        Assert.Equal(ExitCode.Ok, code);
        Assert.Equal(3, _api.ClaimCalls.Count);
        Assert.Equal(2, _clock.Delays);
        Assert.Equal([(Token, false)], _connector.Installs);
        Assert.Equal(Phase.Connected, _store.State!.Phase);
        Assert.Equal("acme-sales.techn0.dpdns.org", _store.State.Hostname);
        Assert.Contains("owner@example.com", _out.ToString());
    }

    [Fact]
    public async Task Never_prints_or_stores_the_tunnel_token()
    {
        _api.Enrolls.Enqueue(new EnrollResult(EnrollOutcome.Requested));
        _api.Claims.Enqueue(Approved());

        await Flow().EnrollAsync(Enroll(), default);

        Assert.DoesNotContain(Token, _out.ToString());
        Assert.DoesNotContain(Token, System.Text.Json.JsonSerializer.Serialize(_store.State));
    }

    [Fact]
    public async Task Never_prints_the_claim_secret_or_the_api_key()
    {
        _api.Enrolls.Enqueue(new EnrollResult(EnrollOutcome.Requested));
        _api.Claims.Enqueue(Approved());

        await Flow().EnrollAsync(Enroll(), default);

        Assert.DoesNotContain(_store.State!.ClaimSecret, _out.ToString());
        Assert.DoesNotContain(ApiKey, _out.ToString());
    }

    [Fact]
    public async Task Sends_only_the_hash_of_the_secret_when_enrolling()
    {
        _api.Enrolls.Enqueue(new EnrollResult(EnrollOutcome.Requested));
        _api.Claims.Enqueue(Approved());

        await Flow().EnrollAsync(Enroll(), default);

        var sent = _api.EnrollCalls.Single();
        var secret = _store.State!.ClaimSecret;

        Assert.Equal(DeviceIdentity.Sha256Hex(secret), sent.Request.ClaimHash);
        Assert.NotEqual(secret, sent.Request.ClaimHash);
        Assert.Equal(secret, _api.ClaimCalls.Single().Secret);
        Assert.Equal("owner@example.com", sent.Request.ContactEmail);
        Assert.Equal("https://bytebalancetech.com", sent.Server);
    }

    [Fact]
    public async Task Remembers_the_secret_before_the_request_goes_out()
    {
        _api.Enrolls.Enqueue(new EnrollmentException("down", transient: true));

        await Assert.ThrowsAsync<EnrollmentException>(
            () => Flow().EnrollAsync(Enroll(), default));

        Assert.NotNull(_store.State);
        Assert.Equal(Phase.Requested, _store.State!.Phase);
    }

    [Fact]
    public async Task Running_again_keeps_the_same_secret_and_device()
    {
        _api.Enrolls.Enqueue(new EnrollmentException("down", transient: true));
        await Assert.ThrowsAsync<EnrollmentException>(
            () => Flow().EnrollAsync(Enroll(), default));

        var first = _store.State!;

        _api.Enrolls.Clear();
        _api.Enrolls.Enqueue(new EnrollResult(EnrollOutcome.AlreadyPending));
        _api.Claims.Enqueue(Approved());

        await Flow().EnrollAsync(Enroll(), default);

        var second = _api.EnrollCalls.Last().Request;

        Assert.Equal(DeviceIdentity.Sha256Hex(first.ClaimSecret), second.ClaimHash);
        Assert.Equal(first.DeviceKey, second.DeviceKey);
        Assert.Equal(first.ClaimSecret, _api.ClaimCalls.Single().Secret);
    }

    [Fact]
    public async Task No_wait_returns_waiting_and_installs_nothing()
    {
        _api.Enrolls.Enqueue(new EnrollResult(EnrollOutcome.Requested));

        var code = await Flow().EnrollAsync(Enroll(wait: false), default);

        Assert.Equal(ExitCode.Waiting, code);
        Assert.Empty(_api.ClaimCalls);
        Assert.Empty(_connector.Installs);
    }

    [Fact]
    public async Task Gives_up_waiting_after_the_timeout_without_failing()
    {
        _api.Enrolls.Enqueue(new EnrollResult(EnrollOutcome.Requested));
        _api.Claims.Enqueue(new ClaimResult(ClaimStatus.Pending));

        var code = await Flow().EnrollAsync(
            Enroll() with { Timeout = TimeSpan.FromMinutes(1) }, default);

        Assert.Equal(ExitCode.Waiting, code);
        Assert.Empty(_connector.Installs);
        Assert.Equal(Phase.Requested, _store.State!.Phase);
    }

    [Theory]
    [InlineData(ClaimStatus.Rejected)]
    [InlineData(ClaimStatus.Revoked)]
    public async Task A_refusal_stops_at_once(ClaimStatus status)
    {
        _api.Enrolls.Enqueue(new EnrollResult(EnrollOutcome.Requested));
        _api.Claims.Enqueue(new ClaimResult(status));

        var code = await Flow().EnrollAsync(Enroll(), default);

        Assert.Equal(ExitCode.Error, code);
        Assert.Single(_api.ClaimCalls);
        Assert.Empty(_connector.Installs);
    }

    [Fact]
    public async Task A_device_decided_as_approved_goes_straight_to_the_claim()
    {
        _api.Enrolls.Enqueue(new EnrollResult(EnrollOutcome.Decided, "approved"));
        _api.Claims.Enqueue(Approved());

        var code = await Flow().EnrollAsync(Enroll(), default);

        Assert.Equal(ExitCode.Ok, code);
        Assert.Single(_connector.Installs);
    }

    [Theory]
    [InlineData("rejected")]
    [InlineData("revoked")]
    public async Task A_device_decided_against_is_told_so(string decided)
    {
        _api.Enrolls.Enqueue(new EnrollResult(EnrollOutcome.Decided, decided));

        var error = await Assert.ThrowsAsync<EnrollmentException>(
            () => Flow().EnrollAsync(Enroll(), default));

        Assert.Contains(decided, error.Message);
        Assert.Empty(_api.ClaimCalls);
    }

    [Fact]
    public async Task A_briefly_unreachable_server_does_not_end_the_wait()
    {
        _api.Enrolls.Enqueue(new EnrollResult(EnrollOutcome.Requested));
        _api.Claims.Enqueue(new EnrollmentException("Could not reach the server.", transient: true));
        _api.Claims.Enqueue(Approved());

        var code = await Flow().EnrollAsync(Enroll(), default);

        Assert.Equal(ExitCode.Ok, code);
        Assert.Contains("Retrying", _out.ToString());
    }

    [Fact]
    public async Task A_permanent_claim_error_is_not_retried()
    {
        _api.Enrolls.Enqueue(new EnrollResult(EnrollOutcome.Requested));
        _api.Claims.Enqueue(new EnrollmentException("not recognised"));

        await Assert.ThrowsAsync<EnrollmentException>(
            () => Flow().EnrollAsync(Enroll(), default));

        Assert.Single(_api.ClaimCalls);
    }

    [Fact]
    public async Task Already_connected_does_nothing()
    {
        _store.State = Saved(Phase.Connected);

        var code = await Flow().EnrollAsync(Enroll(), default);

        Assert.Equal(ExitCode.Ok, code);
        Assert.Empty(_api.EnrollCalls);
        Assert.Empty(_connector.Installs);
    }

    [Fact]
    public async Task Another_email_cannot_take_over_a_pending_enrolment()
    {
        _store.State = Saved();

        var error = await Assert.ThrowsAsync<EnrollmentException>(
            () => Flow().EnrollAsync(Enroll(email: "someone-else@example.com"), default));

        Assert.Contains("owner@example.com", error.Message);
        Assert.Empty(_api.EnrollCalls);
    }

    [Fact]
    public async Task The_same_email_in_another_case_is_the_same_owner()
    {
        _store.State = Saved(email: "Owner@Example.com");

        _api.Enrolls.Enqueue(new EnrollResult(EnrollOutcome.AlreadyPending));
        _api.Claims.Enqueue(Approved());

        var code = await Flow().EnrollAsync(Enroll(email: "owner@example.com"), default);

        Assert.Equal(ExitCode.Ok, code);
    }

    [Fact]
    public async Task A_gateway_that_is_not_listening_warns_but_does_not_stop_enrolment()
    {
        _gateway.Listening = false;
        _api.Enrolls.Enqueue(new EnrollResult(EnrollOutcome.Requested));
        _api.Claims.Enqueue(Approved());

        var code = await Flow().EnrollAsync(Enroll(), default);

        Assert.Equal(ExitCode.Ok, code);
        Assert.Contains("502", _out.ToString());
    }

    [Fact]
    public async Task A_failed_connector_install_keeps_the_enrolment_so_claim_can_retry()
    {
        _api.Enrolls.Enqueue(new EnrollResult(EnrollOutcome.Requested));
        _api.Claims.Enqueue(Approved());
        _connector.FailWith = new EnrollmentException("cloudflared is not installed.");

        await Assert.ThrowsAsync<EnrollmentException>(
            () => Flow().EnrollAsync(Enroll(), default));

        // Saved before the connector was touched, so the retry knows the
        // half-installed service is this enrolment's own.
        Assert.Equal(Phase.Installing, _store.State!.Phase);
        Assert.Empty(_api.UploadCalls);

        _connector.FailWith = null;
        _connector.State = ConnectorState.Stopped;

        var code = await Flow().ClaimAsync(
            new ClaimOptions(TimeSpan.FromMinutes(1), false), default);

        Assert.Equal(ExitCode.Ok, code);
        Assert.Equal(Phase.Connected, _store.State.Phase);
        Assert.True(_connector.Installs.Single().Replace);
    }

    [Fact]
    public async Task A_connector_that_is_not_ours_is_refused_before_the_gateway_is_changed()
    {
        _store.State = Saved();
        _connector.State = ConnectorState.Running;
        _api.Claims.Enqueue(Approved());

        var error = await Assert.ThrowsAsync<EnrollmentException>(
            () => Flow().ClaimAsync(new ClaimOptions(TimeSpan.FromMinutes(1), false), default));

        Assert.True(error.ConnectorConflict);
        Assert.Null(_gateway.Required);
        Assert.Equal(Phase.Requested, _store.State!.Phase);
        Assert.Empty(_connector.Installs);
    }

    [Fact]
    public async Task Claim_without_an_enrolment_says_to_enrol_first()
    {
        var error = await Assert.ThrowsAsync<EnrollmentException>(
            () => Flow().ClaimAsync(new ClaimOptions(TimeSpan.FromMinutes(1), false), default));

        Assert.Contains("enroll", error.Message);
    }

    [Fact]
    public async Task Claim_uses_the_server_saved_at_enrolment()
    {
        _store.State = Saved(server: "https://example.test");

        _api.Claims.Enqueue(Approved());

        await Flow().ClaimAsync(new ClaimOptions(TimeSpan.FromMinutes(1), true), default);

        Assert.Equal("https://example.test", _api.ClaimCalls.Single().Server);
        Assert.True(_connector.Installs.Single().Replace);
    }

    /* ---- the gateway is told to check the tunnel's token ---- */

    [Fact]
    public async Task The_gateway_is_told_to_require_the_access_token_before_the_tunnel_comes_up()
    {
        _api.Enrolls.Enqueue(new EnrollResult(EnrollOutcome.Requested));
        _api.Claims.Enqueue(Approved());
        _connector.OnInstall = () => _gateway.Required;

        await Flow().EnrollAsync(Enroll(), default);

        Assert.Equal(
            ("team.cloudflareaccess.com", "aud-tag-123"),
            ((string, string)?)_connector.SeenAtInstall);
    }

    [Fact]
    public async Task Login_set_up_for_another_application_is_left_alone_and_said_so()
    {
        _gateway.AcceptEdgeAccess = false;
        _api.Enrolls.Enqueue(new EnrollResult(EnrollOutcome.Requested));
        _api.Claims.Enqueue(Approved());

        var code = await Flow().EnrollAsync(Enroll(), default);

        Assert.Equal(ExitCode.Ok, code);
        Assert.Null(_gateway.Required);
        Assert.Contains("different application", _out.ToString());
        Assert.Single(_connector.Installs);
    }

    [Fact]
    public async Task A_server_that_does_not_name_the_access_application_is_noted_not_fatal()
    {
        _api.Enrolls.Enqueue(new EnrollResult(EnrollOutcome.Requested));
        _api.Claims.Enqueue(new ClaimResult(ClaimStatus.Approved, "h.techn0.dpdns.org", Token));

        var code = await Flow().EnrollAsync(Enroll(), default);

        Assert.Equal(ExitCode.Ok, code);
        Assert.Null(_gateway.Required);
        Assert.Contains("did not say which Access", _out.ToString());
    }

    /* ---- the API key goes to ByteBalance ---- */

    [Fact]
    public async Task The_api_key_is_shared_once_the_tunnel_is_up()
    {
        _api.Enrolls.Enqueue(new EnrollResult(EnrollOutcome.Requested));
        _api.Claims.Enqueue(Approved());

        await Flow().EnrollAsync(Enroll(), default);

        var upload = _api.UploadCalls.Single();

        Assert.Equal(ApiKey, upload.Key);
        Assert.Equal(_store.State!.ClaimSecret, upload.Secret);
        Assert.Equal(_store.State.DeviceKey, upload.DeviceKey);
        Assert.NotNull(_store.State.KeySharedAt);
    }

    [Fact]
    public async Task A_key_that_cannot_be_shared_is_a_warning_not_a_failed_enrolment()
    {
        _api.Enrolls.Enqueue(new EnrollResult(EnrollOutcome.Requested));
        _api.Claims.Enqueue(Approved());
        _api.Uploads.Enqueue(new EnrollmentException("Key storage is not configured."));

        var code = await Flow().EnrollAsync(Enroll(), default);

        Assert.Equal(ExitCode.Ok, code);
        Assert.Equal(Phase.Connected, _store.State!.Phase);
        Assert.Null(_store.State.KeySharedAt);
        Assert.Contains("sync-key", _out.ToString());
    }

    [Fact]
    public async Task Sync_key_sends_the_current_key_and_records_it()
    {
        _store.State = Saved(Phase.Connected);
        _gateway.ApiKey = "a-brand-new-key";

        var code = await Flow().SyncKeyAsync(default);

        Assert.Equal(ExitCode.Ok, code);
        Assert.Equal("a-brand-new-key", _api.UploadCalls.Single().Key);
        Assert.NotNull(_store.State!.KeySharedAt);
    }

    [Fact]
    public async Task Sync_key_needs_a_connected_machine()
    {
        _store.State = Saved();

        await Assert.ThrowsAsync<EnrollmentException>(() => Flow().SyncKeyAsync(default));

        Assert.Empty(_api.UploadCalls);
    }

    [Fact]
    public async Task Sync_key_refuses_to_send_an_empty_key()
    {
        _store.State = Saved(Phase.Connected);
        _gateway.ApiKey = string.Empty;

        await Assert.ThrowsAsync<EnrollmentException>(() => Flow().SyncKeyAsync(default));

        Assert.Empty(_api.UploadCalls);
    }

    [Fact]
    public async Task After_a_key_rotation_the_new_key_follows_and_a_failure_only_warns()
    {
        _store.State = Saved(Phase.Connected);
        _api.Uploads.Enqueue(new EnrollmentException("Could not reach the server.", transient: true));

        var result = await Flow().TrySyncKeyAsync(default);

        Assert.False(result);
        Assert.Contains("still has the old API key", _out.ToString());
    }

    [Fact]
    public async Task After_a_key_rotation_on_a_machine_that_is_not_enrolled_nothing_happens()
    {
        var result = await Flow().TrySyncKeyAsync(default);

        Assert.Null(result);
        Assert.Empty(_api.UploadCalls);
        Assert.Equal(string.Empty, _out.ToString());
    }

    /* ---- leaving ---- */

    [Fact]
    public async Task Unenroll_removes_the_connector_the_requirement_and_the_state()
    {
        _store.State = Saved(Phase.Connected) with { EdgeAccessOwned = true };

        var code = await Flow().UnenrollAsync(default);

        Assert.Equal(ExitCode.Ok, code);
        Assert.Equal(1, _connector.Uninstalls);
        Assert.True(_gateway.Cleared);
        Assert.True(_store.Cleared);
        Assert.Contains("administrator", _out.ToString());
    }

    [Fact]
    public async Task Unenroll_removes_a_connector_whose_install_never_finished()
    {
        _store.State = Saved(Phase.Installing);

        await Flow().UnenrollAsync(default);

        Assert.Equal(1, _connector.Uninstalls);
        Assert.True(_store.Cleared);
    }

    [Fact]
    public async Task Unenroll_leaves_an_access_requirement_the_administrator_already_had()
    {
        _gateway.AlreadyRequired = true;
        _store.State = Saved();
        _api.Claims.Enqueue(Approved());

        await Flow().ClaimAsync(new ClaimOptions(TimeSpan.FromMinutes(1), false), default);

        Assert.False(_store.State!.EdgeAccessOwned);

        await Flow().UnenrollAsync(default);

        Assert.False(_gateway.Cleared);
        Assert.True(_store.Cleared);
    }

    [Fact]
    public async Task Unenroll_takes_back_the_access_requirement_that_enrolment_added()
    {
        _store.State = Saved();
        _api.Claims.Enqueue(Approved());

        await Flow().ClaimAsync(new ClaimOptions(TimeSpan.FromMinutes(1), false), default);

        Assert.True(_store.State!.EdgeAccessOwned);

        await Flow().UnenrollAsync(default);

        Assert.True(_gateway.Cleared);
    }

    [Fact]
    public async Task Unenroll_before_connecting_leaves_cloudflared_alone()
    {
        _store.State = Saved();

        await Flow().UnenrollAsync(default);

        Assert.Equal(0, _connector.Uninstalls);
        Assert.True(_store.Cleared);
    }

    [Fact]
    public async Task Unenroll_on_a_machine_that_is_not_enrolled_is_harmless()
    {
        var code = await Flow().UnenrollAsync(default);

        Assert.Equal(ExitCode.Ok, code);
        Assert.Equal(0, _connector.Uninstalls);
    }

    /* ---- status ---- */

    [Fact]
    public async Task Status_shows_the_state_without_any_secret()
    {
        _store.State = Saved(Phase.Connected) with { KeySharedAt = _clock.Now };
        _connector.State = ConnectorState.Running;

        await Flow().StatusAsync(default);

        var text = _out.ToString();

        Assert.Contains("acme.techn0.dpdns.org", text);
        Assert.Contains("service running", text);
        Assert.Contains("shared with ByteBalance", text);
        Assert.DoesNotContain("the-claim-secret", text);
        Assert.DoesNotContain(ApiKey, text);
    }

    [Fact]
    public async Task Status_flags_a_key_that_was_never_shared()
    {
        _store.State = Saved(Phase.Connected);

        await Flow().StatusAsync(default);

        Assert.Contains("NOT shared", _out.ToString());
    }

    [Fact]
    public async Task Status_before_enrolling_points_at_enroll()
    {
        await Flow().StatusAsync(default);

        Assert.Contains("enroll", _out.ToString());
    }
}
