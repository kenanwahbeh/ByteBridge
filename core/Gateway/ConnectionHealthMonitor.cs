using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ByteBridge.Configuration;
using ByteBridge.Data;

namespace ByteBridge.Gateway;

/*
 * Keeps each enabled connection's LastTestSuccessful in step with
 * whether its database actually answers.
 *
 * /databases reports "online" from that flag, and until now only the
 * desktop window ever wrote it, at the moment someone turned a
 * connection on. A connection enabled any other way, or one whose
 * server was down at that test and came back later, stayed "online":
 * false while queries against it worked. Probing on a timer makes the
 * flag a fact about now instead of a note about one past click.
 */
public sealed class ConnectionHealthMonitor
{
    private readonly SqliteDatabase _database;

    private readonly Func<DatabaseConfig, CancellationToken, Task<(bool Succeeded, string? Error)>> _test;

    public ConnectionHealthMonitor(
        SqliteDatabase database,
        Func<DatabaseConfig, CancellationToken, Task<(bool Succeeded, string? Error)>>? test = null)
    {
        _database = database;
        _test = test ?? DatabaseConnectionTester.TestAsync;
    }

    /*
     * Probes every enabled connection once and records the result.
     * Disabled connections are left alone: they are offline by choice
     * and their last result is what the window shows in gray.
     *
     * Returns the names whose state flipped, for the caller to log.
     */
    public async Task<IReadOnlyList<(string Name, bool Online)>> RefreshAsync(
        CancellationToken cancellationToken = default)
    {
        var changes = new List<(string, bool)>();

        foreach (var connection in _database.GetConnections().Where(c => c.Enabled))
        {
            cancellationToken.ThrowIfCancellationRequested();

            /*
             * Skipped, not probed. A connection whose engine this build
             * cannot serve is not a database that is down, and marking
             * it failed every minute would report a configuration
             * problem as an outage. It keeps whatever test result it
             * already had until an administrator saves a supported
             * engine.
             */
            if (!connection.EngineIsSupported)
            {
                continue;
            }

            var (succeeded, _) = await _test(connection, cancellationToken);

            _database.SetTestResult(connection.Id, succeeded);

            if (succeeded != connection.LastTestSuccessful)
            {
                changes.Add((connection.Name, succeeded));
            }
        }

        return changes;
    }
}
