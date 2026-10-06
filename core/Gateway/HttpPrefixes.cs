namespace ByteBridge.Gateway;

/*
 * Which prefixes may be handed to HTTP.SYS.
 *
 * The service runs as Local System and shells out to
 * "netsh http add urlacl" with the prefix as an argument. That is a
 * privilege, granted to the service by the operator, and the one thing
 * that must never happen is a prefix that makes the gateway reachable
 * from off the machine.
 *
 * The check was inline in service/UrlReservation.cs, which is a Windows
 * project the test suite cannot reference, so the guard that stands
 * between a mistyped setting and an exposed listener could not be asked
 * a single question by a test.
 *
 * Whitespace is refused as well, and that is new rather than inherited:
 * Uri parses "http://127.0.0.1:8080/ http://0.0.0.0:9090/" as a
 * loopback URI with a space in the path, so the loopback answer alone
 * says yes to a prefix with a second address inside it. No real prefix
 * has a space, and a reservation no listener can ever match is a
 * SYSTEM-owned entry nobody will think to remove.
 */
public static class HttpPrefixes
{
    public static bool IsReservablePrefix(string? prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix)
            || prefix.Any(char.IsWhiteSpace))
        {
            return false;
        }

        if (!Uri.TryCreate(prefix, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && uri.IsLoopback;
    }
}