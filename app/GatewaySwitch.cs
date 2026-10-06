using System;
using System.ComponentModel;
using System.Threading.Tasks;
using ByteBridge.Configuration;
using ByteBridge.Data;
using ByteBridge.Gateway;
using ByteBridge.Localization;

namespace ByteBridge;

/*
 * The one switch for the gateway.
 *
 * There used to be two that meant almost the same thing: a setting that
 * told the service whether to listen, and the service itself, started
 * from one dialog and stoppable from nowhere in the app. Someone who
 * stopped the service and reopened the app found the gateway down with
 * no obvious way to bring it back.
 *
 * Turning it on now records that the gateway should be listening and
 * starts the service; turning it off records that it should not and
 * stops the service. The setting keeps the intent across a reboot, so
 * "off" stays off and "on" comes back with Windows.
 *
 * Each returns the message to show, or null when it worked.
 */
public sealed class GatewaySwitch
{
    private readonly SqliteDatabase _database;

    private readonly GatewayServiceControl _service;

    public GatewaySwitch(
        SqliteDatabase database,
        GatewayServiceControl service)
    {
        _database = database;
        _service = service;
    }

    public async Task<string?> TurnOnAsync(int? port = null)
    {
        /*
         * Refused before anything is saved. The settings loader ignores a
         * port outside 1 to 65535, so keeping one would have started the
         * gateway on the old port and reported that it worked.
         */
        if (port is int asked && !GatewayConfig.IsValidPort(asked))
        {
            return Strings.Get("InvalidPort");
        }

        if (_service.State() == ServiceState.NotInstalled)
        {
            return Strings.Get("ServiceNotInstalledMessage");
        }

        var config = _database.GetGatewayConfig();

        if (port is { } chosen)
        {
            config.Port = chosen;
        }

        config.AutoStart = true;

        _database.SaveGatewayConfig(config);

        try
        {
            // Waits on Windows for up to twenty seconds, so not on the UI thread.
            await Task.Run(() => _service.Start());

            return null;
        }
        catch (Exception error) when (ServiceFailures.IsServiceFailure(error))
        {
            return Strings.Format("ServiceError", error.Message);
        }
    }

    public async Task<string?> TurnOffAsync()
    {
        var config = _database.GetGatewayConfig();

        config.AutoStart = false;

        _database.SaveGatewayConfig(config);

        if (_service.State() is not (ServiceState.Running or ServiceState.Pending))
        {
            return null;
        }

        try
        {
            await Task.Run(() => _service.Stop());

            return null;
        }
        catch (Exception error) when (ServiceFailures.IsServiceFailure(error))
        {
            return Strings.Format("ServiceStopError", error.Message);
        }
    }

}
