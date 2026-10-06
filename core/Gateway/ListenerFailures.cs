using System.Net;

namespace ByteBridge.Gateway;

/*
 * The Windows listener's own error codes, and the one of them that means
 * something other than what it says.
 *
 * Both numbers were literals in two files that have to agree and never
 * could be told apart: the worker decided whether to reserve a URL from
 * 5, and the server wrote 5 into the message that tells the operator what
 * to do about it. A third literal, 183, is the other name for the same
 * "that port is taken" that 32 already covers.
 */
public static class ListenerFailures
{
    /*
     * HTTP.SYS's "Access is denied", which means there is no reservation
     * for this prefix -- not anything about the file system.
     */
    public const int AccessDenied = 5;

    public const int AddressInUse = 32;

    public const int AddressAlreadyInUse = 183;

    public static bool IsAddressInUse(int errorCode) =>
        errorCode is AddressInUse or AddressAlreadyInUse;

    /*
     * Whether this failure is HTTP.SYS refusing a prefix it has no
     * reservation for, which the service can fix by making the
     * reservation and starting once more.
     *
     * The whole chain is walked rather than one level of InnerException,
     * because the one level was a fact about how GatewayServer happens
     * to wrap today: add a second wrapper and the retry stops firing
     * with nothing to show for it, and the gateway never recovers from a
     * port change without an elevated prompt.
     */
    public static bool NeedsUrlReservation(Exception? error)
    {
        for (var current = error; current != null; current = current.InnerException)
        {
            if (current is HttpListenerException listener
                && listener.ErrorCode == AccessDenied)
            {
                return true;
            }
        }

        return false;
    }
}