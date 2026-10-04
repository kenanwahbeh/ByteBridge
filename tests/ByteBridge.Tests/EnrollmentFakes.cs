using System.Net;
using System.Text;
using ByteBridge.Enrollment;

namespace ByteBridge.Tests;

/*
 * Stand-ins for everything the enrolment touches outside this process:
 * the ByteBalance server, Windows services, cloudflared, the clock. None
 * of the enrolment tests need a network, a service or administrator
 * rights.
 */
internal static class EnrollmentFakes
{
    /* Time that only moves when something waits, so polling costs nothing. */
    public sealed class Clock : IClock
    {
        public DateTimeOffset Now { get; private set; } =
            new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

        public int Delays { get; private set; }

        public Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Now += duration;
            Delays++;
            return Task.CompletedTask;
        }
    }

    public sealed class Machine : IMachineId
    {
        public string Get() => "11111111-2222-3333-4444-555555555555";
    }

    public sealed class MemoryStore : IEnrollmentStore
    {
        public EnrollmentState? State { get; set; }

        public bool Cleared { get; private set; }

        public EnrollmentState? Load() => State;

        public void Save(EnrollmentState state) => State = state;

        public void Clear()
        {
            State = null;
            Cleared = true;
        }
    }

    public sealed class FakeGateway : IGatewaySettings
    {
        public string ApiKey { get; set; } = "the-gateway-api-key";

        public bool Listening { get; set; } = true;

        public string Address => "http://127.0.0.1:8080";

        /* False makes it behave as if login were set up for another app. */
        public bool AcceptEdgeAccess { get; set; } = true;

        public (string Team, string Audience)? Required { get; private set; }

        public bool Cleared { get; private set; }

        public bool TryRequireEdgeAccess(string teamDomain, string audience)
        {
            if (!AcceptEdgeAccess)
            {
                return false;
            }

            Required = (teamDomain, audience);
            return true;
        }

        public void ClearEdgeAccess()
        {
            Required = null;
            Cleared = true;
        }
    }

    public sealed class Connector : IConnector
    {
        public ConnectorState State { get; set; } = ConnectorState.NoService;

        public List<(string Token, bool Replace)> Installs { get; } = [];

        public int Uninstalls { get; private set; }

        public Exception? FailWith { get; set; }

        /* Records what the gateway had been told at the moment of install. */
        public Func<object?>? OnInstall { get; set; }

        public object? SeenAtInstall { get; private set; }

        public ConnectorState Inspect() => State;

        public Task InstallAsync(string tunnelToken, bool replaceExisting, CancellationToken cancellationToken)
        {
            if (FailWith != null)
            {
                throw FailWith;
            }

            SeenAtInstall = OnInstall?.Invoke();
            Installs.Add((tunnelToken, replaceExisting));
            State = ConnectorState.Running;

            return Task.CompletedTask;
        }

        public Task UninstallAsync(CancellationToken cancellationToken)
        {
            Uninstalls++;
            State = ConnectorState.NoService;

            return Task.CompletedTask;
        }
    }

    /* Scripted answers, in order; the last one repeats. */
    public sealed class Api : IEnrollmentApi
    {
        public Queue<object> Enrolls { get; } = new();

        public Queue<object> Claims { get; } = new();

        public Queue<object> Uploads { get; } = new();

        public List<(string Server, EnrollRequest Request)> EnrollCalls { get; } = [];

        public List<(string Server, string DeviceKey, string Secret)> ClaimCalls { get; } = [];

        public List<(string Server, string DeviceKey, string Secret, string Key)> UploadCalls { get; } = [];

        public Task<EnrollResult> EnrollAsync(string server, EnrollRequest request, CancellationToken cancellationToken)
        {
            EnrollCalls.Add((server, request));
            return Task.FromResult((EnrollResult)Next(Enrolls)!);
        }

        public Task<ClaimResult> ClaimAsync(string server, string deviceKey, string claimSecret, CancellationToken cancellationToken)
        {
            ClaimCalls.Add((server, deviceKey, claimSecret));
            return Task.FromResult((ClaimResult)Next(Claims)!);
        }

        public Task UploadKeyAsync(string server, string deviceKey, string claimSecret, string apiKey, CancellationToken cancellationToken)
        {
            UploadCalls.Add((server, deviceKey, claimSecret, apiKey));

            if (Uploads.Count > 0)
            {
                Next(Uploads);
            }

            return Task.CompletedTask;
        }

        private static object? Next(Queue<object> queue)
        {
            var item = queue.Count > 1 ? queue.Dequeue() : queue.Peek();

            return item is Exception error ? throw error : item;
        }
    }

    public sealed class Runner : IProcessRunner
    {
        public List<(string File, string[] Arguments)> Calls { get; } = [];

        public Func<string, string[], ProcessResult> Handler { get; set; } =
            (_, _) => new ProcessResult(0, string.Empty);

        public Task<ProcessResult> RunAsync(string file, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        {
            var args = arguments.ToArray();
            Calls.Add((file, args));
            return Task.FromResult(Handler(file, args));
        }
    }

    /* Answers whatever the test hands it, and records what was sent. */
    public sealed class Handler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public List<(Uri? Uri, string Body)> Requests { get; } = [];

        public Handler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            _respond = respond;
        }

        public static Handler Json(HttpStatusCode status, string json) =>
            new(_ => new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add((
                request.RequestUri,
                request.Content == null
                    ? string.Empty
                    : await request.Content.ReadAsStringAsync(cancellationToken)));

            return _respond(request);
        }
    }

    public static EnrollmentServices Services(
        Api api,
        Connector connector,
        Clock clock,
        TextWriter output,
        TextWriter error,
        bool elevated = true) =>
        new()
        {
            Api = api,
            Connector = connector,
            Clock = clock,
            Machine = new Machine(),
            IsElevated = () => elevated,
            Out = output,
            Error = error
        };
}
