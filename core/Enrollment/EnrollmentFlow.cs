using System.Runtime.InteropServices;

namespace ByteBridge.Enrollment;

public sealed record EnrollOptions(
    string Server,
    string Email,
    string Name,
    bool Wait,
    TimeSpan Timeout,
    bool ReplaceConnector);

public sealed record ClaimOptions(
    TimeSpan Timeout,
    bool ReplaceConnector);

public static class ExitCode
{
    public const int Ok = 0;
    public const int Error = 1;

    /*
     * Not a failure: the request is with the approver. Scripts can tell
     * "try again later" from "something is wrong".
     */
    public const int Waiting = 2;
}

/*
 * The whole enrolment, in the order it has to happen:
 *
 *   1. Remember a fresh claim secret, before asking for anything, so a
 *      crash after the request cannot leave the server holding a hash
 *      nobody can answer for.
 *   2. Ask the control plane for a tunnel (this emails the approver).
 *   3. Wait until it is approved.
 *   4. Collect the connector token, make the gateway require the
 *      tunnel's Access token, and give the token to cloudflared. In that
 *      order, so the tunnel never comes up before the gateway is ready
 *      to check it.
 *   5. Share the API key with ByteBalance so the storefront can call
 *      this gateway.
 *
 * Every step can be repeated. Running it again after a crash, a timeout
 * or a closed window continues where it stopped and never starts a second
 * request, because the server treats a second request from the same
 * machine as the same device.
 */
public sealed class EnrollmentFlow
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);

    private readonly IEnrollmentApi _api;
    private readonly IEnrollmentStore _store;
    private readonly IConnector _connector;
    private readonly IGatewaySettings _gateway;
    private readonly IMachineId _machine;
    private readonly IClock _clock;
    private readonly TextWriter _out;

    public EnrollmentFlow(
        IEnrollmentApi api,
        IEnrollmentStore store,
        IConnector connector,
        IGatewaySettings gateway,
        IMachineId machine,
        IClock clock,
        TextWriter output)
    {
        _api = api;
        _store = store;
        _connector = connector;
        _gateway = gateway;
        _machine = machine;
        _clock = clock;
        _out = output;
    }

    public async Task<int> EnrollAsync(
        EnrollOptions options,
        CancellationToken cancellationToken)
    {
        var state = _store.Load();

        if (state?.Phase == Phase.Connected)
        {
            _out.WriteLine(
                $"Already connected: {state.Hostname}. Nothing to do.");

            return ExitCode.Ok;
        }

        if (state != null)
        {
            /*
             * The owner email is what Cloudflare Access will admit, so it
             * cannot be changed by enrolling again with another address:
             * that would be a way to take over a pending device.
             */
            if (!SameEmail(state.Email, options.Email)
                || !SameServer(state.Server, options.Server))
            {
                throw new EnrollmentException(
                    $"This machine is already enrolled for {state.Email} "
                    + $"at {state.Server}. Run `claim` to continue that "
                    + "enrolment, or run `unenroll` (after the ByteBalance "
                    + "administrator has removed the device) to start again.");
            }
        }
        else
        {
            state = new EnrollmentState(
                Server: options.Server,
                DeviceKey: DeviceIdentity.DeviceKey(_machine.Get()),
                ClaimSecret: DeviceIdentity.NewClaimSecret(),
                Name: options.Name,
                Email: options.Email,
                Phase: Phase.Requested,
                EnrolledAt: _clock.Now);

            _store.Create(state);
        }

        WarnIfNotListening();

        var request = new EnrollRequest(
            state.DeviceKey,
            state.Name,
            state.Email,
            DeviceIdentity.Sha256Hex(state.ClaimSecret),
            Fingerprint());

        var result = await _api.EnrollAsync(state.Server, request, cancellationToken);

        switch (result.Outcome)
        {
            case EnrollOutcome.Requested:
                _out.WriteLine(
                    "Request sent. ByteBalance has been asked to approve this machine.");
                break;

            case EnrollOutcome.AlreadyPending:
                _out.WriteLine(
                    "This machine already has a request waiting for approval.");
                break;

            case EnrollOutcome.Decided:
                /*
                 * Approved earlier and then interrupted is the normal
                 * way to get here, so it carries on to the claim below.
                 * Refused or revoked does not get better by waiting.
                 */
                if (result.DecidedStatus != "approved")
                {
                    throw new EnrollmentException(
                        $"This machine was {result.DecidedStatus} by ByteBalance. "
                        + "Ask the administrator to remove the device before enrolling again.");
                }

                break;
        }

        if (!options.Wait)
        {
            _out.WriteLine(
                "Not waiting. Once it is approved, run: ByteBridge.Service.exe claim");

            return ExitCode.Waiting;
        }

        return await ClaimCoreAsync(
            state,
            options.Timeout,
            options.ReplaceConnector,
            cancellationToken);
    }

    public async Task<int> ClaimAsync(
        ClaimOptions options,
        CancellationToken cancellationToken)
    {
        var state = _store.Load()
            ?? throw new EnrollmentException(
                "This machine has not been enrolled. Run `enroll` first.");

        if (state.Phase == Phase.Connected)
        {
            _out.WriteLine(
                $"Already connected: {state.Hostname}. Nothing to do.");

            return ExitCode.Ok;
        }

        return await ClaimCoreAsync(
            state,
            options.Timeout,
            options.ReplaceConnector,
            cancellationToken);
    }

    /*
     * Sends the current API key to ByteBalance again. Needed after the key
     * is replaced, or when the first attempt failed.
     */
    public async Task<int> SyncKeyAsync(CancellationToken cancellationToken)
    {
        var state = _store.Load();

        if (state?.Phase != Phase.Connected)
        {
            throw new EnrollmentException(
                "This machine is not connected to ByteBalance, so there is "
                + "nowhere to send the key. Run `enroll` first.");
        }

        await ShareKeyAsync(state, cancellationToken, announce: true);

        return ExitCode.Ok;
    }

    /*
     * For the key-rotation path: never throws, because the key has
     * already been replaced locally and that must not look like a
     * failure. Returns whether ByteBalance has the new one.
     */
    public async Task<bool?> TrySyncKeyAsync(CancellationToken cancellationToken)
    {
        EnrollmentState? state;

        try
        {
            state = _store.Load();
        }
        catch (EnrollmentException)
        {
            return false;
        }

        if (state?.Phase != Phase.Connected)
        {
            return null;
        }

        try
        {
            await ShareKeyAsync(state, cancellationToken, announce: true);

            return true;
        }
        catch (EnrollmentException error)
        {
            // The old "shared at" time would otherwise keep saying the
            // storefront has the key when it holds a stale one.
            try
            {
                _store.Save(state with { KeySharedAt = null });
            }
            catch (EnrollmentException)
            {
            }

            _out.WriteLine(
                "warning: ByteBalance still has the old API key, so the "
                + $"storefront cannot reach this gateway: {error.Message} "
                + "Run `sync-key` when the server is reachable.");

            return false;
        }
    }

    /*
     * Forgets the enrolment on this machine: removes the connector this
     * enrolment installed, stops requiring the tunnel's Access token, and
     * clears the saved state.
     *
     * It cannot tell ByteBalance. The device stays approved there until
     * the administrator removes it, and this machine cannot enrol again
     * until then, because the secret that proved it is the same one now
     * being deleted.
     */
    public async Task<int> UnenrollAsync(CancellationToken cancellationToken)
    {
        EnrollmentState? state;

        try
        {
            state = _store.Load();
        }
        catch (EnrollmentException)
        {
            // Half-saved settings: this is the recovery path the message
            // names, so it has to work on exactly that. Without a readable
            // record there is no telling what this enrolment changed, so
            // the connector and the Access setting stay as they are.
            _store.Clear();

            _out.WriteLine(
                "The incomplete enrolment settings were cleared. The "
                + "Cloudflare connector and the gateway's Access setting, if "
                + "any, were left as they are.");

            return ExitCode.Ok;
        }

        if (state == null)
        {
            _out.WriteLine("This machine is not enrolled.");
            return ExitCode.Ok;
        }

        /*
         * Installing counts: the connector may be there even though the
         * install never reached Connected, and leaving it running would
         * keep a tunnel to this gateway open after "disconnect".
         */
        if (state.Phase is Phase.Installing or Phase.Connected)
        {
            await _connector.UninstallAsync(cancellationToken);
            _out.WriteLine("The Cloudflare connector was removed.");
        }

        if (state.EdgeAccessOwned)
        {
            _gateway.ClearEdgeAccess();
        }

        _store.Clear();

        _out.WriteLine(
            "Enrolment forgotten on this machine. Ask the ByteBalance "
            + "administrator to remove the device too; until they do, this "
            + "machine cannot enrol again.");

        return ExitCode.Ok;
    }

    public Task<int> StatusAsync(CancellationToken cancellationToken)
    {
        var state = _store.Load();

        if (state == null)
        {
            _out.WriteLine(
                "enrolment  none. Run: ByteBridge.Service.exe enroll --email <you@example.com>");
        }
        else
        {
            _out.WriteLine(
                "enrolment  "
                + state.Phase switch
                {
                    Phase.Connected => "connected",
                    Phase.Installing =>
                        "approved, but the connector did not finish installing. Run: claim",
                    _ => "waiting for approval"
                });
            _out.WriteLine($"server     {state.Server}");
            _out.WriteLine($"device     {state.Name} ({state.DeviceKey})");
            _out.WriteLine($"owner      {state.Email}");

            if (state.Hostname != null)
            {
                _out.WriteLine($"hostname   {state.Hostname}");
            }

            if (state.Phase == Phase.Connected)
            {
                _out.WriteLine(
                    state.KeySharedAt is { } shared
                        ? $"api key    shared with ByteBalance {shared:yyyy-MM-dd HH:mm} UTC"
                        : "api key    NOT shared with ByteBalance yet. Run: sync-key");
            }
        }

        _out.WriteLine($"connector  {Describe(_connector.Inspect())}");

        return Task.FromResult(ExitCode.Ok);
    }

    private async Task<int> ClaimCoreAsync(
        EnrollmentState state,
        TimeSpan timeout,
        bool replaceConnector,
        CancellationToken cancellationToken)
    {
        var deadline = _clock.Now + timeout;
        var announced = false;

        while (true)
        {
            ClaimResult claim;

            try
            {
                claim = await _api.ClaimAsync(
                    state.Server,
                    state.DeviceKey,
                    state.ClaimSecret,
                    cancellationToken);
            }
            catch (EnrollmentException error) when (error.Transient)
            {
                /*
                 * A server that is briefly unreachable must not end a
                 * wait that may be minutes from succeeding.
                 */
                _out.WriteLine($"{error.Message} Retrying.");

                claim = new ClaimResult(ClaimStatus.Pending);
            }

            switch (claim.Status)
            {
                case ClaimStatus.Approved:
                    return await ConnectAsync(
                        state,
                        claim,
                        replaceConnector,
                        cancellationToken);

                case ClaimStatus.Rejected:
                case ClaimStatus.Revoked:
                    _out.WriteLine(
                        $"ByteBalance {claim.Status.ToString().ToLowerInvariant()} "
                        + "this machine. Nothing was installed.");

                    return ExitCode.Error;
            }

            if (!announced)
            {
                _out.WriteLine(
                    "Waiting for approval. This can take a while; it is safe to "
                    + "close this and run `claim` later.");

                announced = true;
            }

            if (_clock.Now + PollInterval > deadline)
            {
                _out.WriteLine(
                    "Still waiting. Run `ByteBridge.Service.exe claim` again later.");

                return ExitCode.Waiting;
            }

            await _clock.DelayAsync(PollInterval, cancellationToken);
        }
    }

    private async Task<int> ConnectAsync(
        EnrollmentState state,
        ClaimResult claim,
        bool replaceConnector,
        CancellationToken cancellationToken)
    {
        /*
         * A connector this enrolment already began installing is its own,
         * so a retry replaces it without being told to.
         */
        var replace = replaceConnector || state.Phase == Phase.Installing;

        /*
         * Before anything is changed: an install that is going to be
         * refused must not leave the gateway demanding a token for a
         * tunnel that was never made.
         */
        await _connector.EnsureCanInstallAsync(replace, cancellationToken);

        /*
         * Then the gateway, so the tunnel is never up while it would
         * still take a request that did not come through Access.
         */
        var owned = ApplyEdgeAccess(claim) || state.EdgeAccessOwned;

        /*
         * Written before the connector is touched, so `unenroll` removes
         * it even if this stops half way.
         */
        state = state with { Phase = Phase.Installing, EdgeAccessOwned = owned };

        _store.Save(state);

        await _connector.InstallAsync(
            claim.TunnelToken!,
            replace,
            cancellationToken);

        state = state with
        {
            Phase = Phase.Connected,
            Hostname = claim.Hostname,
            ConnectedAt = _clock.Now
        };

        try
        {
            _store.Save(state);
        }
        catch (EnrollmentException)
        {
            // `unenroll` ran while this was installing: take back the
            // connector it could not have known was finished.
            await _connector.UninstallAsync(CancellationToken.None);

            throw;
        }

        _out.WriteLine($"Connected: {claim.Hostname}");
        _out.WriteLine(
            $"Only {state.Email} can open it; Cloudflare sends that address a "
            + "one-time code to sign in.");

        /*
         * The tunnel is up either way. A key that could not be shared
         * yet is said clearly and fixed with `sync-key`, not turned into
         * a failure of everything that already worked.
         */
        try
        {
            await ShareKeyAsync(state, cancellationToken, announce: true);
        }
        catch (EnrollmentException error)
        {
            _out.WriteLine(
                $"warning: the API key could not be shared with ByteBalance: {error.Message} "
                + "The storefront cannot reach this gateway until you run `sync-key`.");
        }

        return ExitCode.Ok;
    }

    /* Returns whether this call is what turned the requirement on. */
    private bool ApplyEdgeAccess(ClaimResult claim)
    {
        if (claim.AccessTeamDomain == null || claim.AccessAudience == null)
        {
            _out.WriteLine(
                "Note: this ByteBalance server did not say which Access "
                + "application protects the tunnel, so the gateway will not "
                + "check the token itself. Cloudflare still enforces it.");

            return false;
        }

        switch (_gateway.RequireEdgeAccess(
                    claim.AccessTeamDomain,
                    claim.AccessAudience))
        {
            case EdgeAccessResult.Applied:
                _out.WriteLine(
                    "The gateway now also requires the tunnel's Cloudflare Access "
                    + "token on requests that arrive through Cloudflare.");

                return true;

            case EdgeAccessResult.AlreadyRequired:
                _out.WriteLine(
                    "The gateway already requires the tunnel's Cloudflare "
                    + "Access token, so that setting was left as it is.");

                return false;

            default:
                _out.WriteLine(
                    "Note: Cloudflare Access login is already set up for a "
                    + "different application on this gateway, so it was left "
                    + "alone and the gateway will not check the tunnel's token "
                    + "itself. Cloudflare still enforces it.");

                return false;
        }
    }

    private async Task ShareKeyAsync(
        EnrollmentState state,
        CancellationToken cancellationToken,
        bool announce)
    {
        var key = _gateway.ApiKey;

        if (string.IsNullOrEmpty(key))
        {
            throw new EnrollmentException(
                "This gateway has no API key yet. Start the service once, "
                + "or run `key new`, then try again.");
        }

        await _api.UploadKeyAsync(
            state.Server,
            state.DeviceKey,
            state.ClaimSecret,
            key,
            cancellationToken);

        _store.Save(state with { KeySharedAt = _clock.Now });

        if (announce)
        {
            _out.WriteLine("The API key was shared with ByteBalance.");
        }
    }

    private void WarnIfNotListening()
    {
        if (!_gateway.Listening)
        {
            _out.WriteLine(
                $"Warning: the gateway is set not to listen on {_gateway.Address}. "
                + "The tunnel will return 502 until you run: ByteBridge.Service.exe on");
        }
    }

    private static Dictionary<string, string> Fingerprint() => new()
    {
        ["hostname"] = Environment.MachineName,
        ["os"] = RuntimeInformation.OSDescription,
        ["agent"] = "bytebridge-"
            + (typeof(EnrollmentFlow).Assembly.GetName().Version?.ToString(3)
               ?? "unknown")
    };

    private static string Describe(ConnectorState state) => state switch
    {
        ConnectorState.NotInstalled => "cloudflared is not installed",
        ConnectorState.NoService => "installed, no tunnel service",
        ConnectorState.Stopped => "service installed, stopped",
        _ => "service running"
    };

    private static bool SameEmail(string a, string b) =>
        string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool SameServer(string a, string b) =>
        string.Equals(
            a.TrimEnd('/'),
            b.TrimEnd('/'),
            StringComparison.OrdinalIgnoreCase);
}
