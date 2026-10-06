using ByteBridge.Configuration;

namespace ByteBridge.Gateway;

/*
 * What the two windows say about the gateway, decided once.
 *
 * The same four-way question -- answering, turned off, starting, or not
 * running -- was answered twice, in MainWindow and in WebServerWindow,
 * from the same three inputs and with the same branch order. Two copies
 * is one copy too many: the Web Server dialog adds a fifth thing to it,
 * a hint line keyed by the same four outcomes, and a field that decides
 * whether the toggle reads "on".
 *
 * The disagreement that had already grown out of it: the main page labels
 * the service toggle from the service state alone, while the dialog asks
 * whether the gateway is meant to be listening at all. With AutoStart off
 * and the service running -- a normal state, and the one the operator
 * chose -- the main page offered Stop and the dialog offered Turn On.
 *
 * Which cannot be fixed by a test either, since both were private
 * methods on windows. So the answer lives here, and the windows read it.
 */
public readonly record struct GatewayStatus(
    string TextKey,
    string HintKey)
{
    /*
     * The key, not the text, because the text is formatted: "Answering"
     * is a format string that takes the base URL, and a caller that
     * remembered to call Format on some of these would produce
     * "Answering" with a brace in it.
     */
    public bool IsAnswering { get; init; }

    public static GatewayStatus For(
        GatewayConfig config,
        ServiceState state,
        bool answering)
    {
        if (answering)
        {
            return new GatewayStatus("Answering", "TunnelHint")
            {
                IsAnswering = true
            };
        }

        /*
         * Off because the setting says so, which is checked before the
         * service state: a service that is up while the gateway was
         * turned off is not starting, and saying "Starting" there would
         * wait for something nobody asked for.
         */
        if (!config.AutoStart)
        {
            return new GatewayStatus("TurnedOff", "TurnedOffHint");
        }

        if (state == ServiceState.Running)
        {
            return new GatewayStatus("Starting", "StartingHint");
        }

        return new GatewayStatus("NotRunning", "NotRunningHint");
    }
}

/*
 * What the main page says about the service itself, which is a different
 * question from the gateway's: the service can be up perfectly well
 * while the gateway inside it refuses to bind.
 */
public static class ServiceStatus
{
    public static string KeyFor(ServiceState state) =>
        state switch
        {
            ServiceState.Running => "ServiceRunning",
            ServiceState.Stopped => "ServiceStopped",
            ServiceState.Pending => "ServicePending",
            _ => "ServiceNotInstalled"
        };
}

/*
 * What the panel should do when it opens.
 *
 * The gate was two negations in a row inside a private method that also
 * showed a MessageBox and started the service, so the one condition in
 * it that could be wrong -- nag somebody who deliberately turned the
 * gateway off, every single launch -- could not be reached by a test.
 */
public enum LaunchAdvice
{
    Nothing,
    ServiceMissing,
    OfferToStart
}

public static class ServiceLaunch
{
    public static LaunchAdvice For(ServiceState state, bool autoStart) =>
        state switch
        {
            ServiceState.NotInstalled => LaunchAdvice.ServiceMissing,
            ServiceState.Stopped when autoStart => LaunchAdvice.OfferToStart,
            _ => LaunchAdvice.Nothing
        };
}