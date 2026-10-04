using ByteBridge.Configuration;
using ByteBridge.Gateway;

namespace ByteBridge.Data;

/*
 * Whether a database's connection details actually work, before they
 * are saved. Public (unlike the providers, which the gateway keeps to
 * itself) because both the desktop window's "turn online" flow and the
 * add/edit wizard need it, and duplicating a connection-string builder
 * in two places is how they'd quietly drift apart.
 */
public static class DatabaseConnectionTester
{
    public static Task<(bool Succeeded, string? Error)> TestAsync(
        DatabaseConfig config,
        CancellationToken cancellationToken = default) =>
        SqlProviders.For(config).TestAsync(config, cancellationToken);
}
