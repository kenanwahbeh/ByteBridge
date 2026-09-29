using System.Net;
using System.Text.Json;
using Xunit;

namespace ByteBridge.Tests;

/*
 * /stats backs the request-count shown on each connection card in the
 * window. It only needs to be right, not fast, so the same poll-until
 * pattern as RequestLoggingTests is used: the count is updated after
 * the response is already on its way back to the caller.
 */
public class GatewayStatsTests
{
    private static async Task<long> CountFor(
        GatewayHarness gateway,
        string database,
        long expected)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var (_, body) = await GatewayHarness.Read(gateway.Get("/stats"));

            var requests = JsonDocument.Parse(body)
                .RootElement
                .GetProperty("requests");

            var seen =
                requests.TryGetProperty(database, out var count)
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
     * /stats names every connection, so it is behind the key like the
     * rest of the API. It used to be open on the reasoning that it held
     * no data, but the names alone are something to keep off the
     * internet.
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

        await CountFor(gateway, "Archive", expected: 1);
        await Task.Delay(200);

        var (_, body) = await GatewayHarness.Read(gateway.Get("/stats"));

        Assert.Equal("{\"requests\":{\"Archive\":1}}", body);
    }

    [Fact]
    public async Task A_request_by_id_is_counted_under_the_connection_name()
    {
        using var gateway = new GatewayHarness();

        await GatewayHarness.Read(gateway.Post("/query", new
        {
            database = gateway.Offline.Id,
            sql = "SELECT 1 FROM RDB$DATABASE",
        }));

        Assert.Equal(1, await CountFor(gateway, "Archive", expected: 1));
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

        Assert.Equal(2, await CountFor(gateway, "Archive", expected: 2));
    }

    [Fact]
    public async Task Requests_that_never_name_a_database_are_not_counted()
    {
        using var gateway = new GatewayHarness();

        await GatewayHarness.Read(gateway.Get("/health", key: ""));
        await GatewayHarness.Read(gateway.Get("/databases"));

        var (_, body) = await GatewayHarness.Read(gateway.Get("/stats"));

        Assert.Equal("{\"requests\":{}}", body);
    }
}
