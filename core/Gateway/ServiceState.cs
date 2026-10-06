namespace ByteBridge.Gateway;

/*
 * The control panel's view of the service that hosts the gateway.
 *
 * Moved here from the panel so the decisions built on it can be tested
 * at all: the enum lived in app/GatewayServiceControl.cs, which is a
 * net10.0-windows project the test suite cannot reference, so every
 * answer derived from it was unreachable -- and there were two of them,
 * in two windows, that had come to disagree.
 */
public enum ServiceState
{
    NotInstalled,
    Stopped,
    Running,
    Pending
}