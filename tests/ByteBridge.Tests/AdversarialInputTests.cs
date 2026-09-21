using System.Net;
using System.Text;
using Xunit;

namespace ByteBridge.Tests;

/*
 * The .NET equivalent of fuzzing an HTTP API against its schema
 * (schemathesis, in the Python world this project's QA baseline came
 * from): systematic malformed, boundary and hostile requests against
 * every gateway endpoint. None of these touch a real Firebird server --
 * every case here is expected to be rejected by the gateway's own
 * validation, before it would ever open a connection. The point is
 * that a hostile or malformed request gets a clean 4xx, never a 500,
 * a hang, or a crash of the server.
 */
public class AdversarialInputTests
{
    [Fact]
    public async Task An_empty_body_is_rejected_not_crashed()
    {
        using var gateway = new GatewayHarness();

        var (status, body) = await GatewayHarness.Read(
            SendRaw(gateway, "/query", Array.Empty<byte>()));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("JSON", body);
    }

    [Fact]
    public async Task A_literal_json_null_body_is_rejected()
    {
        using var gateway = new GatewayHarness();

        var (status, _) = await GatewayHarness.Read(
            SendRaw(gateway, "/query", "null"u8.ToArray()));

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Fact]
    public async Task Malformed_json_is_rejected_with_the_parse_error()
    {
        using var gateway = new GatewayHarness();

        var (status, body) = await GatewayHarness.Read(
            SendRaw(gateway, "/query", "{ \"sql\": "u8.ToArray()));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("Invalid JSON", body);
    }

    [Fact]
    public async Task Non_utf8_bytes_do_not_crash_the_server()
    {
        using var gateway = new GatewayHarness();

        // A lone continuation byte: not valid UTF-8, not valid JSON.
        var garbage = new byte[] { 0xC3, 0x28, 0xA0, 0x80 };

        var (status, _) = await GatewayHarness.Read(
            SendRaw(gateway, "/query", garbage));

        Assert.Equal(HttpStatusCode.BadRequest, status);

        // The server is still answering after choking on garbage bytes.
        var (health, _) = await GatewayHarness.Read(gateway.Get("/health", key: ""));
        Assert.Equal(HttpStatusCode.OK, health);
    }

    [Fact]
    public async Task A_body_over_the_size_cap_is_rejected_with_413()
    {
        using var gateway = new GatewayHarness();

        // One byte past GatewayServer's 1 MB cap.
        var oversized = new byte[(1024 * 1024) + 1];
        Array.Fill(oversized, (byte)'a');

        var (status, _) = await GatewayHarness.Read(
            SendRaw(gateway, "/query", oversized));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, status);
    }

    [Fact]
    public async Task Deeply_nested_json_is_rejected_not_crashed()
    {
        using var gateway = new GatewayHarness();

        // Past System.Text.Json's default 64-level depth limit.
        var nested = new StringBuilder();
        nested.Append("{\"sql\":\"SELECT 1\",\"parameters\":");
        nested.Append('[', 200);
        nested.Append(']', 200);
        nested.Append('}');

        var (status, body) = await GatewayHarness.Read(
            SendRaw(gateway, "/query", Encoding.UTF8.GetBytes(nested.ToString())));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("Invalid JSON", body);
    }

    [Fact]
    public async Task Parameters_of_the_wrong_json_shape_are_rejected_not_crashed()
    {
        using var gateway = new GatewayHarness();

        // "parameters" is a named-parameter object, not an array.
        const string payload =
            "{\"database\":\"Sales\",\"sql\":\"SELECT 1\",\"parameters\":[1,2,3]}";

        var (status, _) = await GatewayHarness.Read(
            SendRaw(gateway, "/query", Encoding.UTF8.GetBytes(payload)));

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Fact]
    public async Task A_wrong_content_type_header_does_not_block_a_valid_body()
    {
        using var gateway = new GatewayHarness();

        var request = new HttpRequestMessage(HttpMethod.Post, "/query")
        {
            Content = new ByteArrayContent(
                Encoding.UTF8.GetBytes("{\"database\":\"Archive\",\"sql\":\"\"}"))
        };
        request.Content.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");
        request.Headers.Add("X-API-Key", gateway.ApiKey);

        using var response = await gateway.Client.SendAsync(request);

        // Rejected because "sql" is empty, not because of the content type.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("sql", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Missing_sql_is_rejected()
    {
        using var gateway = new GatewayHarness();

        var (status, body) = await GatewayHarness.Read(gateway.Post("/query", new
        {
            database = "Sales",
        }));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("sql", body);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public async Task Blank_sql_is_rejected(string sql)
    {
        using var gateway = new GatewayHarness();

        var (status, _) = await GatewayHarness.Read(gateway.Post("/query", new
        {
            database = "Sales",
            sql,
        }));

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Theory]
    [InlineData("DELETE FROM Customers")]
    [InlineData("DROP TABLE Customers")]
    [InlineData("UPDATE Customers SET Name = 'x'")]
    public async Task A_write_statement_sent_to_query_is_rejected(string sql)
    {
        using var gateway = new GatewayHarness();

        var (status, body) = await GatewayHarness.Read(gateway.Post("/query", new
        {
            database = "Sales",
            sql,
        }));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("/execute", body);
    }

    [Fact]
    public async Task Missing_database_is_rejected()
    {
        using var gateway = new GatewayHarness();

        var (status, body) = await GatewayHarness.Read(gateway.Post("/query", new
        {
            sql = "SELECT 1",
        }));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("database", body);
    }

    [Fact]
    public async Task An_unknown_database_name_returns_404_not_an_error()
    {
        using var gateway = new GatewayHarness();

        var (status, _) = await GatewayHarness.Read(gateway.Post("/query", new
        {
            database = "NoSuchConnection",
            sql = "SELECT 1",
        }));

        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    /*
     * An injection attempt against the connection lookup itself, not
     * against SQL: "database" is only ever compared to configured names
     * and ids, never interpolated into a statement, so this can only
     * ever miss and 404.
     */
    [Fact]
    public async Task A_sql_injection_shaped_database_name_is_just_a_failed_lookup()
    {
        using var gateway = new GatewayHarness();

        var (status, _) = await GatewayHarness.Read(gateway.Post("/query", new
        {
            database = "Sales'; DROP TABLE Customers; --",
            sql = "SELECT 1",
        }));

        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    /*
     * The offline connection is rejected before the gateway ever tries
     * to reach Firebird, so this is reachable without a live server.
     */
    [Fact]
    public async Task A_disabled_connection_returns_409_without_touching_firebird()
    {
        using var gateway = new GatewayHarness();

        var (status, body) = await GatewayHarness.Read(gateway.Post("/query", new
        {
            database = gateway.Offline.Name,
            sql = "SELECT 1",
        }));

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Contains("offline", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_very_large_sql_string_under_the_body_cap_is_rejected_cleanly()
    {
        using var gateway = new GatewayHarness();

        // Well under the 1 MB body cap, but not a SELECT/WITH -- rejected
        // for that reason, proving the parser and the keyword check both
        // survive a large-but-legal string without crashing or hanging.
        var hugeSql = new string('x', 900_000);

        var (status, _) = await GatewayHarness.Read(gateway.Post("/query", new
        {
            database = "Sales",
            sql = hugeSql,
        }));

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Fact]
    public async Task Get_to_a_post_only_endpoint_is_rejected_with_405()
    {
        using var gateway = new GatewayHarness();

        var (status, _) = await GatewayHarness.Read(gateway.Get("/query"));

        Assert.Equal(HttpStatusCode.MethodNotAllowed, status);
    }

    [Fact]
    public async Task Post_to_a_get_only_endpoint_is_rejected_with_405()
    {
        using var gateway = new GatewayHarness();

        var (status, _) = await GatewayHarness.Read(gateway.Post("/databases", body: null));

        Assert.Equal(HttpStatusCode.MethodNotAllowed, status);
    }

    [Fact]
    public async Task An_unknown_path_returns_404()
    {
        using var gateway = new GatewayHarness();

        var (status, _) = await GatewayHarness.Read(gateway.Get("/this-route-does-not-exist"));

        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    [Fact]
    public async Task An_absurdly_long_api_key_is_rejected_not_crashed()
    {
        using var gateway = new GatewayHarness();

        // Comfortably under HttpListener/http.sys's own header-size
        // limit, so this exercises the gateway's own key comparison
        // rather than a lower-level protocol rejection.
        var (status, _) = await GatewayHarness.Read(
            gateway.Get("/databases", key: new string('k', 4000)));

        Assert.Equal(HttpStatusCode.Unauthorized, status);

        // The server is still answering after a giant header.
        var (health, _) = await GatewayHarness.Read(gateway.Get("/health", key: ""));
        Assert.Equal(HttpStatusCode.OK, health);
    }

    [Fact]
    public async Task An_options_preflight_needs_no_api_key()
    {
        using var gateway = new GatewayHarness();

        var request = new HttpRequestMessage(HttpMethod.Options, "/query");

        using var response = await gateway.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Auth_login_without_oauth_configured_is_rejected_not_crashed()
    {
        using var gateway = new GatewayHarness();

        var (status, body) = await GatewayHarness.Read(gateway.Get("/auth/login", key: ""));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("OAuth", body);
    }

    [Fact]
    public async Task Auth_callback_without_oauth_configured_is_rejected_not_crashed()
    {
        using var gateway = new GatewayHarness();

        var (status, _) = await GatewayHarness.Read(
            gateway.Get("/auth/callback?code=anything&state=anything", key: ""));

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Fact]
    public async Task Auth_me_without_a_session_is_unauthenticated_not_crashed()
    {
        using var gateway = new GatewayHarness();

        var (status, _) = await GatewayHarness.Read(gateway.Get("/auth/me", key: ""));

        Assert.Equal(HttpStatusCode.Unauthorized, status);
    }

    [Fact]
    public async Task An_unknown_auth_endpoint_returns_404()
    {
        using var gateway = new GatewayHarness();

        var (status, _) = await GatewayHarness.Read(gateway.Get("/auth/not-a-real-endpoint", key: ""));

        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    private static Task<HttpResponseMessage> SendRaw(
        GatewayHarness gateway,
        string path,
        byte[] body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new ByteArrayContent(body)
        };
        request.Content.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        request.Headers.Add("X-API-Key", gateway.ApiKey);

        return gateway.Client.SendAsync(request);
    }
}
