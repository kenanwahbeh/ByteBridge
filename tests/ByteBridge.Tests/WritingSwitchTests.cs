using System.Net;
using Xunit;

namespace ByteBridge.Tests;

/*
 * The gateway answers reads and nothing else until an administrator has
 * turned writing on at the machine. These pin the default, that the
 * switch takes effect on the next request with no restart, and what the
 * gateway says about itself while it is off.
 */
public class WritingSwitchTests
{
    /*
     * "Nope" matches no connection, so the switch is the only thing
     * under test: 403 means the request stopped at it, 404 means it got
     * past and went looking for a connection.
     */
    private static readonly object WriteToNowhere =
        new { database = "Nope", sql = "DELETE FROM CUSTOMERS" };

    [Fact]
    public void A_fresh_install_only_reads()
    {
        using var gateway = new GatewayHarness();

        Assert.False(gateway.Database.GetAllowWrites());
    }

    [Fact]
    public async Task Execute_is_refused_while_writing_is_off()
    {
        using var gateway = new GatewayHarness();

        var (status, body) = await GatewayHarness.Read(
            gateway.Post("/execute", WriteToNowhere));

        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.Contains("read-only", body);
    }

    [Fact]
    public async Task Turning_writing_on_takes_effect_on_the_next_request()
    {
        using var gateway = new GatewayHarness();

        gateway.Database.SetAllowWrites(true);

        var (status, _) = await GatewayHarness.Read(
            gateway.Post("/execute", WriteToNowhere));

        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    [Fact]
    public async Task Turning_it_off_again_refuses_again()
    {
        using var gateway = new GatewayHarness();

        gateway.Database.SetAllowWrites(true);
        gateway.Database.SetAllowWrites(false);

        var (status, _) = await GatewayHarness.Read(
            gateway.Post("/execute", WriteToNowhere));

        Assert.Equal(HttpStatusCode.Forbidden, status);
    }

    [Fact]
    public async Task A_wrong_key_is_still_unauthorized_not_forbidden()
    {
        using var gateway = new GatewayHarness();

        var (status, _) = await GatewayHarness.Read(
            gateway.Post("/execute", WriteToNowhere, key: "not-the-key"));

        Assert.Equal(HttpStatusCode.Unauthorized, status);
    }

    [Fact]
    public async Task The_endpoint_list_names_execute_only_while_writing_is_on()
    {
        using var gateway = new GatewayHarness();

        var (_, off) = await GatewayHarness.Read(gateway.Get("/nope"));

        Assert.DoesNotContain("/execute", off);

        gateway.Database.SetAllowWrites(true);

        var (_, on) = await GatewayHarness.Read(gateway.Get("/nope"));

        Assert.Contains("/execute", on);
    }

    [Fact]
    public async Task Query_points_a_write_at_execute_only_while_writing_is_on()
    {
        using var gateway = new GatewayHarness();

        var write = new { database = "Sales", sql = "DELETE FROM CUSTOMERS" };

        var (_, off) = await GatewayHarness.Read(gateway.Post("/query", write));

        Assert.DoesNotContain("/execute", off);

        gateway.Database.SetAllowWrites(true);

        var (_, on) = await GatewayHarness.Read(gateway.Post("/query", write));

        Assert.Contains("/execute", on);
    }

    [Fact]
    public void Saving_the_gateway_settings_does_not_undo_the_switch()
    {
        using var root = new TempDataRoot();
        var database = root.OpenDatabase();

        // What the control panel does: read the settings, then save them
        // back -- here after the switch has moved in between.
        var stale = database.GetGatewayConfig();

        database.SetAllowWrites(true);
        database.SaveGatewayConfig(stale);

        Assert.True(database.GetAllowWrites());
    }
}
