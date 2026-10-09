using System.Net;
using System.Text.Json;
using Xunit;

namespace ByteBridge.Tests;

/*
 * /stats backs the request-count shown on each connection card in the
 * window, keyed by connection id. It only needs to be right, not fast,
 * so the same poll-until pattern as RequestLoggingTests is used: the
 * count is updated after the response is already on its way back to
 * the caller.
 */
public class GatewayStatsTests
{
    private static async Task<long> CountFor(
        GatewayHarness gateway,
        string connectionId,
        long expected)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var (_, body) = await GatewayHarness.Read(gateway.Get("/stats"));

            var requests = JsonDocument.Parse(body)
                .RootElement
                .GetProperty("requests");

            var seen =
                requests.TryGetProperty(connectionId, out var count)
                    ? count.GetInt64()
                    : 0;

            if (seen >= expected)
            {
                return seen;
            }

            await Task.Delay(50);
        }

        return 0;
    }

    /*
     * /stats lists every connection, so it is behind the key like the
     * rest of the API. It used to be open on the reasoning that it held
     * no data, but a list of what is behind the tunnel is something to
     * keep off the internet.
     */
    [Fact]
    public async Task Stats_requires_the_api_key()
    {
        using var gateway = new GatewayHarness();

        var (missing, _) = await GatewayHarness.Read(gateway.Get("/stats", key: ""));
        var (wrong, _) = await GatewayHarness.Read(gateway.Get("/stats", key: "not-the-key"));
        var (right, _) = await GatewayHarness.Read(gateway.Get("/stats"));

        Assert.Equal(HttpStatusCode.Unauthorized, missing);
        Assert.Equal(HttpStatusCode.Unauthorized, wrong);
        Assert.Equal(HttpStatusCode.OK, right);
    }

    /*
     * The name a caller types is not a key. Counting it let one client
     * fill the table with names that match nothing, and /stats would
     * then have listed them back.
     */
    [Fact]
    public async Task A_name_that_matches_no_connection_is_not_counted()
    {
        using var gateway = new GatewayHarness();

        await GatewayHarness.Read(gateway.Post("/query", new
        {
            database = "no-such-connection",
            sql = "SELECT 1 FROM RDB$DATABASE",
        }));

        // A marker request that is counted, so we know the loop has run.
        await GatewayHarness.Read(gateway.Post("/query", new
        {
            database = "Archive",
            sql = "SELECT 1 FROM RDB$DATABASE",
        }));

        await CountFor(gateway, gateway.Offline.Id, expected: 1);
        await Task.Delay(200);

        var (_, body) = await GatewayHarness.Read(gateway.Get("/stats"));

        // The counts are what is under test; /stats carries other fields too.
        Assert.Equal(
            $"{{\"{gateway.Offline.Id}\":1}}",
            JsonDocument.Parse(body).RootElement
                .GetProperty("requests").GetRawText());
    }

    [Fact]
    public async Task A_request_by_id_and_one_by_name_count_toward_the_same_connection()
    {
        using var gateway = new GatewayHarness();

        await GatewayHarness.Read(gateway.Post("/query", new
        {
            database = gateway.Offline.Id,
            sql = "SELECT 1 FROM RDB$DATABASE",
        }));

        await GatewayHarness.Read(gateway.Post("/query", new
        {
            database = "Archive",
            sql = "SELECT 1 FROM RDB$DATABASE",
        }));

        Assert.Equal(2, await CountFor(gateway, gateway.Offline.Id, expected: 2));
    }

    /*
     * A settings file from before names had to be unique can hold two
     * connections with one name. Counting by name would run their
     * requests together and put the same total on both cards.
     */
    [Fact]
    public async Task Two_connections_that_share_a_name_are_counted_apart()
    {
        using var gateway = new GatewayHarness();

        var twin = gateway.AddLegacyConnectionNamed("Archive");

        await GatewayHarness.Read(gateway.Post("/query", new
        {
            database = gateway.Offline.Id,
            sql = "SELECT 1 FROM RDB$DATABASE",
        }));

        for (var i = 0; i < 2; i++)
        {
            await GatewayHarness.Read(gateway.Post("/query", new
            {
                database = twin.Id,
                sql = "SELECT 1 FROM RDB$DATABASE",
            }));
        }

        Assert.Equal(2, await CountFor(gateway, twin.Id, expected: 2));
        Assert.Equal(1, await CountFor(gateway, gateway.Offline.Id, expected: 1));
    }

    [Fact]
    public async Task A_query_increments_the_count_for_its_database()
    {
        using var gateway = new GatewayHarness();

        await GatewayHarness.Read(gateway.Post("/query", new
        {
            database = "Archive",
            sql = "SELECT ID FROM CUSTOMERS WHERE ID = @id",
            parameters = new Dictionary<string, object> { ["id"] = 1 },
        }));

        await GatewayHarness.Read(gateway.Post("/query", new
        {
            database = "Archive",
            sql = "SELECT ID FROM CUSTOMERS WHERE ID = @id",
            parameters = new Dictionary<string, object> { ["id"] = 2 },
        }));

        Assert.Equal(2, await CountFor(gateway, gateway.Offline.Id, expected: 2));
    }

    [Fact]
    public async Task Requests_that_never_name_a_database_are_not_counted()
    {
        using var gateway = new GatewayHarness();

        await GatewayHarness.Read(gateway.Get("/health", key: ""));
        await GatewayHarness.Read(gateway.Get("/databases"));

        var (_, body) = await GatewayHarness.Read(gateway.Get("/stats"));

        Assert.Equal(
            "{}",
            JsonDocument.Parse(body).RootElement
                .GetProperty("requests").GetRawText());
    }
}
