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
            var (_, body) = await GatewayHarness.Read(gateway.Get("/stats", key: ""));

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

    [Fact]
    public async Task Stats_needs_no_api_key()
    {
        using var gateway = new GatewayHarness();

        var (status, _) = await GatewayHarness.Read(gateway.Get("/stats", key: ""));

        Assert.Equal(HttpStatusCode.OK, status);
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

        var (_, body) = await GatewayHarness.Read(gateway.Get("/stats", key: ""));

        Assert.Equal("{\"requests\":{}}", body);
    }
}
