using Xunit;
using ByteBridge.Configuration;
using ByteBridge.Gateway;

namespace ByteBridge.Tests;

public class OAuthSessionManagerTests
{
    [Fact]
    public void Login_state_is_bound_to_the_cookie_and_consumed_once()
    {
        using var root = new TempDataRoot();
        var database = root.OpenDatabase();
        var manager = new OAuthSessionManager(
            new OAuthConfig { Enabled = true },
            database);

        var state = manager.CreateLoginState("client-one");

        Assert.NotNull(state);
        Assert.False(manager.ConsumeLoginState(state, "wrong"));
        Assert.True(manager.ConsumeLoginState(state, state));
        Assert.False(manager.ConsumeLoginState(state, state));
    }

    [Fact]
    public void Login_state_store_has_a_fixed_capacity()
    {
        using var root = new TempDataRoot();
        var database = root.OpenDatabase();
        var manager = new OAuthSessionManager(
            new OAuthConfig { Enabled = true },
            database);

        for (var i = 0; i < 1024; i++)
        {
            Assert.NotNull(
                manager.CreateLoginState($"client-{i}"));
        }

        Assert.Null(manager.CreateLoginState("another-client"));
    }

    [Fact]
    public void One_client_cannot_fill_the_global_login_state_store()
    {
        using var root = new TempDataRoot();
        var database = root.OpenDatabase();
        var manager = new OAuthSessionManager(
            new OAuthConfig { Enabled = true },
            database);

        for (var i = 0; i < 8; i++)
        {
            Assert.NotNull(manager.CreateLoginState("noisy-client"));
        }

        Assert.Null(manager.CreateLoginState("noisy-client"));
        Assert.NotNull(manager.CreateLoginState("other-client"));
    }

    [Fact]
    public void Authentication_cookies_are_secure()
    {
        var sessionCookie = OAuthSessionManager.FormatCookie("token", 60);
        var stateCookie = OAuthSessionManager.FormatLoginStateCookie("state");
        var clearCookie = OAuthSessionManager.ClearCookie();

        Assert.Contains("Secure", sessionCookie);
        Assert.Contains("HttpOnly", sessionCookie);
        Assert.Contains("Secure", stateCookie);
        Assert.Contains("HttpOnly", stateCookie);
        Assert.Contains("Secure", clearCookie);
    }

    [Fact]
    public void Cloudflare_access_cookie_is_extracted()
    {
        var token =
            OAuthSessionManager.ExtractCloudflareAccessTokenFromCookie(
                "other=value; CF_Authorization=jwt-value; final=value");

        Assert.Equal("jwt-value", token);
    }
}
