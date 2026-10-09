using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ByteBridge.Updates;

namespace ByteBridge.Service;

/*
 * Looks for a newer release in the background and writes down what it
 * found, which is all it does.
 *
 * It never downloads or installs anything. A service that runs as the
 * machine's most privileged account and fetches executables on its own
 * would turn a compromise of the release page into code running on every
 * gateway, and these sit in front of databases. The answer goes into the
 * settings file, where the control panel (which offers the Windows
 * installer to a person at the keyboard), the command line and /stats
 * read it.
 *
 * The settings are read each time round, so turning checks off with
 * "update off" takes effect without a restart. How often GitHub is
 * really asked is UpdateService's decision (once a day, remembered
 * across restarts); this loop only wakes up often enough that a service
 * left running for weeks still asks.
 */
public sealed class UpdateWorker : BackgroundService
{
    private static readonly TimeSpan WakeInterval = TimeSpan.FromHours(6);

    private readonly UpdateService _updates;

    private readonly ILogger<UpdateWorker> _logger;

    // The version already announced in the log, so it is said once.
    private ReleaseVersion? _announced;

    public UpdateWorker(
        UpdateService updates,
        ILogger<UpdateWorker> logger)
    {
        _updates = updates;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        try
        {
            /*
             * Not at start-up: a machine that has just booted is busy,
             * its network may not be up, and a fleet restarted together
             * should not ask together.
             */
            await Task.Delay(
                TimeSpan.FromSeconds(60 + Random.Shared.Next(0, 240)),
                stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                await CheckOnceAsync(stoppingToken);

                await Task.Delay(WakeInterval, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Stopping.
        }
    }

    private async Task CheckOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            var status = await _updates.CheckAsync(
                force: false,
                stoppingToken);

            if (status.Error != null)
            {
                _logger.LogInformation(
                    "Could not check for a newer ByteBridge: {Reason}",
                    status.Error);

                return;
            }

            if (status.Available && status.Latest != _announced)
            {
                _announced = status.Latest;

                _logger.LogInformation(
                    "ByteBridge {Latest} is available; this is {Current}. "
                    + "Release notes: {Url}",
                    status.Latest,
                    status.Current,
                    status.Url);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception error)
        {
            /*
             * Whatever went wrong here is not worth the gateway: it is a
             * convenience, and the loop tries again in six hours.
             */
            _logger.LogWarning(
                error,
                "The update check failed unexpectedly.");
        }
    }
}
