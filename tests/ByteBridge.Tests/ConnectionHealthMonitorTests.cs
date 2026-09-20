using Xunit;
using ByteBridge.Configuration;
using ByteBridge.Gateway;

namespace ByteBridge.Tests;

/*
 * /databases used to say "online": false for a connection that was
 * enabled and reachable, because LastTestSuccessful was only ever
 * written by the desktop window. The monitor probes on a timer so the
 * flag follows the database instead of the last click.
 */
public class ConnectionHealthMonitorTests
{
    [Fact]
    public async Task An_enabled_connection_that_answers_becomes_online()
    {
        using var root = new TempDataRoot();
        var database = root.OpenDatabase();

        database.AddConnection(MakeConnection("Sales", enabled: true, lastTestSuccessful: false));

        var monitor = new ConnectionHealthMonitor(
            database,
            _ => Task.FromResult<(bool, string?)>((true, null)));

        var changes = await monitor.RefreshAsync();

        Assert.True(Assert.Single(database.GetConnections()).LastTestSuccessful);
        Assert.Equal(("Sales", true), Assert.Single(changes));
    }

    [Fact]
    public async Task An_enabled_connection_that_stops_answering_becomes_offline()
    {
        using var root = new TempDataRoot();
        var database = root.OpenDatabase();

        database.AddConnection(MakeConnection("Sales", enabled: true, lastTestSuccessful: true));

        var monitor = new ConnectionHealthMonitor(
            database,
            _ => Task.FromResult<(bool, string?)>((false, "connection refused")));

        var changes = await monitor.RefreshAsync();

        Assert.False(Assert.Single(database.GetConnections()).LastTestSuccessful);
        Assert.Equal(("Sales", false), Assert.Single(changes));
    }

    [Fact]
    public async Task A_disabled_connection_is_not_probed()
    {
        using var root = new TempDataRoot();
        var database = root.OpenDatabase();

        database.AddConnection(MakeConnection("Archive", enabled: false, lastTestSuccessful: false));

        var probed = 0;

        var monitor = new ConnectionHealthMonitor(
            database,
            _ =>
            {
                probed++;
                return Task.FromResult<(bool, string?)>((true, null));
            });

        await monitor.RefreshAsync();

        Assert.Equal(0, probed);
        Assert.False(Assert.Single(database.GetConnections()).LastTestSuccessful);
    }

    [Fact]
    public async Task An_unchanged_state_is_not_reported_as_a_change()
    {
        using var root = new TempDataRoot();
        var database = root.OpenDatabase();

        database.AddConnection(MakeConnection("Sales", enabled: true, lastTestSuccessful: true));

        var monitor = new ConnectionHealthMonitor(
            database,
            _ => Task.FromResult<(bool, string?)>((true, null)));

        Assert.Empty(await monitor.RefreshAsync());
    }

    private static DatabaseConfig MakeConnection(
        string name,
        bool enabled,
        bool lastTestSuccessful) => new()
    {
        Name = name,
        Server = "127.0.0.1",
        Port = 3050,
        Username = "SYSDBA",
        Password = "not-a-real-password",
        Database = $"/data/{name.ToLowerInvariant()}.fdb",
        Enabled = enabled,
        LastTestSuccessful = lastTestSuccessful
    };
}
