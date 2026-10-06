using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net;
using ByteBridge.Configuration;
using ByteBridge.Gateway;
using ByteBridge.Localization;
using Xunit;

namespace ByteBridge.Tests;

/*
 * The Windows layer, reached from core.
 *
 * Everything tested here lived in projects the suite cannot reference:
 * service/ is net10.0-windows, and app/ is WPF, so none of it built on
 * Linux and none of it ran in CI. It was reviewed by reading it, which
 * is how the following survived to be written down:
 *
 *   - a prefix guard that decides whether a privilege is granted to
 *     HTTP.SYS as Local System, and had no test at all;
 *   - two windows answering the same four-way question differently;
 *   - "is the gateway on" asked two ways, one of which turned the
 *     setting back on behind the operator's back;
 *   - an email box that closed the control panel when it was used as
 *     intended;
 *   - a login dialog that accepted https:// in a hostname field and
 *     then demanded a token from a JWKS URI that cannot resolve;
 *   - and three lists of fields that had to stay in step with their
 *     types, where dropping a field fails silently.
 *
 * The decisions are in core now. The windows and the service read them,
 * and these are the questions that used to have no one to ask them.
 */
public class WindowsLayerTests
{
    // ---- The reservation the service makes as Local System -------------

    /*
     * The prefix the service hands to netsh, with the right to listen on
     * it, as SYSTEM.
     *
     * Every rejection here is a case where the gateway would be
     * reachable from a machine that is not this one. The setting is
     * supposed to only ever hold a loopback address -- that is what
     * GatewayConfig.Host says and why cloudflared is what reaches the
     * gateway from outside -- so a host that was mistyped, or a prefix
     * that arrived with a scheme, must not become a listening socket on
     * the LAN. There was no test to say so, because the guard was
     * interleaved with Process.Start in a Windows-only class.
     */
    [Theory]
    [InlineData("http://127.0.0.1:8080/")]
    [InlineData("http://localhost:8080/")]
    [InlineData("http://127.0.0.1:8080/some/path")]
    public void A_loopback_http_prefix_may_be_reserved(string prefix)
    {
        Assert.True(HttpPrefixes.IsReservablePrefix(prefix));
    }

    [Theory]
    // Not loopback: the machine's own LAN address, and the address that
    // means every interface at once. Both are how a gateway that was
    // meant to be reached only through the tunnel becomes reachable
    // without it.
    [InlineData("http://0.0.0.0:8080/")]
    [InlineData("http://192.168.1.10:8080/")]
    [InlineData("http://[::]:8080/")]
    // Loopback but not plain HTTP, which the listener is not.
    [InlineData("https://127.0.0.1:8080/")]
    // A prefix with a second one inside it, as netsh would read it.
    [InlineData("http://127.0.0.1:8080/ http://0.0.0.0:9090/")]
    // Not absolute, so there is nothing to bind.
    [InlineData("127.0.0.1:8080/")]
    [InlineData("")]
    [InlineData(null)]
    public void Anything_else_may_not(string? prefix)
    {
        Assert.False(HttpPrefixes.IsReservablePrefix(prefix));
    }

    // ---- The listener's error codes ------------------------------------

    /*
     * What makes the service reserve a URL and try again.
     *
     * This is the whole of "changing the port in the control panel does
     * not need an administrator": the gateway is refused, the code is
     * recognised as HTTP.SYS rather than as anything else, and the
     * reservation is made by the one process allowed to make it. Miss the
     * code and the gateway simply never starts after a port change, with
     * no explanation.
     *
     * The chain is walked rather than one InnerException deep, because
     * that depth was a fact about how GatewayServer wraps today. The
     * fourth case is the shape that wrapping twice would produce.
     */
    [Fact]
    public void Access_denied_anywhere_in_the_chain_means_reserve_and_retry()
    {
        var refused = new HttpListenerException(ListenerFailures.AccessDenied);

        Assert.True(ListenerFailures.NeedsUrlReservation(refused));

        Assert.True(
            ListenerFailures.NeedsUrlReservation(
                new InvalidOperationException("wrapped once", refused)));

        Assert.True(
            ListenerFailures.NeedsUrlReservation(
                new InvalidOperationException(
                    "wrapped twice",
                    new InvalidOperationException("and again", refused))));
    }

    [Theory]
    [InlineData(ListenerFailures.AddressInUse)]
    [InlineData(ListenerFailures.AddressAlreadyInUse)]
    public void A_taken_port_is_not_something_reserving_can_fix(int errorCode)
    {
        // Reserving a URL for a port another program already holds would
        // succeed and change nothing, and the gateway would still not
        // start -- with the log claiming it had tried.
        Assert.False(
            ListenerFailures.NeedsUrlReservation(
                new HttpListenerException(errorCode)));
    }

    [Fact]
    public void An_unrelated_failure_is_not_the_listeners()
    {
        Assert.False(ListenerFailures.NeedsUrlReservation(new IOException()));
        Assert.False(ListenerFailures.NeedsUrlReservation(null));
    }

    /*
     * The two names for "that port is taken", and the one for "no
     * reservation". Three numbers that were literals in two files which
     * had to agree and could not be told apart.
     */
    [Fact]
    public void The_error_codes_are_the_ones_windows_uses()
    {
        Assert.Equal(5, ListenerFailures.AccessDenied);
        Assert.Equal(32, ListenerFailures.AddressInUse);
        Assert.Equal(183, ListenerFailures.AddressAlreadyInUse);

        Assert.True(ListenerFailures.IsAddressInUse(32));
        Assert.True(ListenerFailures.IsAddressInUse(183));

        // Access denied is the one that is not a taken port, which is
        // why the two answers have to be different questions.
        Assert.False(ListenerFailures.IsAddressInUse(
            ListenerFailures.AccessDenied));
    }

    // ---- The three field lists -----------------------------------------

    /*
     * A rebind drops the listener and binds again, so a missing field
     * means a setting nothing can change -- and nothing says so, which is
     * why it is worth a test that fails when someone adds a setting.
     */
    [Fact]
    public void Every_rebind_setting_is_actually_compared()
    {
        var config = new GatewayConfig();
        var running = new GatewayConfig();

        // Every one of them, turned one at a time, has to be seen.
        foreach (var name in GatewayConfig.RebindSettings)
        {
            Assert.False(config.NeedsRebindFrom(running));

            Switch(name, running);

            Assert.True(
                config.NeedsRebindFrom(running),
                $"{name} is listed as a rebind setting but nothing compares it.");

            Set(name, running, changed: false);
        }

        Assert.False(config.NeedsRebindFrom(running));
    }

    /*
     * The exception: the key is swapped into the running gateway without
     * dropping the listener, so rotating it must not cost a rebind. It is
     * a rebind setting that must never be in RebindSettings, so the test
     * above cannot tell the difference on its own.
     */
    [Fact]
    public void Rotating_the_key_does_not_cost_a_rebind()
    {
        var running = new GatewayConfig { ApiKey = "old" };
        var desired = new GatewayConfig { ApiKey = "new" };

        Assert.False(desired.NeedsRebindFrom(running));
        Assert.DoesNotContain(nameof(GatewayConfig.ApiKey), GatewayConfig.RebindSettings);
    }

    [Fact]
    public void Every_compared_oauth_setting_is_actually_compared()
    {
        var config = new OAuthConfig();
        var running = new OAuthConfig();

        foreach (var name in OAuthConfig.ComparedSettings)
        {
            // Identical, so they match. The sense of this is the other
            // way round from the rebind test, where identical means no
            // rebind -- which is how the two can be confused for each
            // other.
            Assert.True(config.Matches(running));

            Switch(name, running);

            Assert.False(
                config.Matches(running),
                $"{name} is listed as compared but nothing compares it.");

            Set(name, running, changed: false);
        }

        Assert.True(config.Matches(running));
    }

    /*
     * A field left off this list is a change that never reaches the
     * running gateway -- and the log line written beside the call says it
     * was applied.
     */
    [Fact]
    public void A_rotated_audience_reaches_the_running_gateway()
    {
        var running = new OAuthConfig { Audience = "old" };
        var desired = new OAuthConfig { Audience = "new" };

        Assert.False(desired.Matches(running));
    }

    // ---- What the windows say ------------------------------------------

    /*
     * The status the two windows derive, in one place.
     *
     * Four outcomes from three inputs, and the order matters: a gateway
     * that is turned off while the service happens to be up is "turned
     * off", not "starting" -- otherwise the panel waits forever for
     * something nobody asked for. Answering beats all of it, because a
     * gateway that is up has demonstrably answered.
     */
    [Fact]
    public void An_answering_gateway_is_answering_what_the_setting_says()
    {
        var config = new GatewayConfig { AutoStart = false };

        var status = GatewayStatus.For(
            config,
            ServiceState.Running,
            answering: true);

        Assert.Equal("Answering", status.TextKey);
        Assert.True(status.IsAnswering);
    }

    [Theory]
    [InlineData(ServiceState.Stopped, true, "NotRunning")]
    [InlineData(ServiceState.Pending, true, "NotRunning")]
    [InlineData(ServiceState.NotInstalled, true, "NotRunning")]
    [InlineData(ServiceState.Stopped, false, "TurnedOff")]
    [InlineData(ServiceState.Running, false, "TurnedOff")]
    [InlineData(ServiceState.Pending, false, "TurnedOff")]
    public void An_off_gateway_is_off_and_not_starting(
        ServiceState state,
        bool autoStart,
        string expected)
    {
        var status = GatewayStatus.For(
            new GatewayConfig { AutoStart = autoStart },
            state,
            answering: false);

        Assert.Equal(expected, status.TextKey);
    }

    /*
     * Service up, meant to be listening, not answering yet: starting.
     * The only case that is neither answering nor off.
     */
    [Fact]
    public void A_service_that_is_up_but_silent_is_starting()
    {
        var status = GatewayStatus.For(
            new GatewayConfig { AutoStart = true },
            ServiceState.Running,
            answering: false);

        Assert.Equal("Starting", status.TextKey);
    }

    /*
     * Every outcome carries a hint, because the Web Server dialog shows
     * one line under the status and an empty line there reads as a bug.
     * The keys are distinct, so the dialog cannot show the wrong advice
     * for the wrong state.
     */
    [Fact]
    public void Every_status_carries_its_own_hint()
    {
        var hints = new[]
        {
            GatewayStatus.For(new GatewayConfig(), ServiceState.Running, true),
            GatewayStatus.For(new GatewayConfig(), ServiceState.Running, false),
            GatewayStatus.For(new GatewayConfig(), ServiceState.Stopped, false),
            GatewayStatus.For(
                new GatewayConfig { AutoStart = false },
                ServiceState.Stopped,
                false)
        }
            .Select(status => status.HintKey)
            .ToList();

        Assert.Equal(hints.Count, hints.Distinct().Count());
        Assert.All(hints, hint => Assert.False(string.IsNullOrWhiteSpace(hint)));
    }

    /*
     * "Is the gateway on" -- the setting and the service, together.
     *
     * The main page asked only about the service, so with the setting
     * off and the service running -- which is what happens when somebody
     * turns the gateway off, and is a perfectly normal state -- it offered
     * to stop a service that was not the gateway. The dialog asked the
     * right question and offered Turn On, and pressing it set AutoStart
     * to true: the operator turned it off, was told to turn it on, and
     * did. One answer, asked once.
     */
    [Theory]
    [InlineData(true, ServiceState.Running, true)]
    [InlineData(true, ServiceState.Stopped, false)]
    [InlineData(true, ServiceState.Pending, false)]
    [InlineData(true, ServiceState.NotInstalled, false)]
    [InlineData(false, ServiceState.Running, false)]
    [InlineData(false, ServiceState.Stopped, false)]
    public void Being_on_needs_both_the_setting_and_the_service(
        bool autoStart,
        ServiceState state,
        bool expected)
    {
        var config = new GatewayConfig { AutoStart = autoStart };

        Assert.Equal(expected, config.GatewayIsOn(state));
    }

    /*
     * The question asked when the panel opens.
     *
     * Two negations in a row, inside a private method that also showed a
     * dialog and started the service, so the condition that would nag
     * somebody who deliberately turned the gateway off on every single
     * launch could not be reached by a test.
     */
    [Theory]
    [InlineData(ServiceState.NotInstalled, true, LaunchAdvice.ServiceMissing)]
    [InlineData(ServiceState.NotInstalled, false, LaunchAdvice.ServiceMissing)]
    [InlineData(ServiceState.Stopped, true, LaunchAdvice.OfferToStart)]
    // Deliberately off, so asking again would be nagging.
    [InlineData(ServiceState.Stopped, false, LaunchAdvice.Nothing)]
    [InlineData(ServiceState.Running, true, LaunchAdvice.Nothing)]
    [InlineData(ServiceState.Running, false, LaunchAdvice.Nothing)]
    [InlineData(ServiceState.Pending, true, LaunchAdvice.Nothing)]
    public void At_launch_the_panel_offers_only_what_it_should(
        ServiceState state,
        bool autoStart,
        LaunchAdvice expected)
    {
        Assert.Equal(expected, ServiceLaunch.For(state, autoStart));
    }

    // ---- Which failures are the service's ------------------------------

    /*
     * Every exception the service control can raise is built by the
     * framework, so this classification decided what to tell the operator
     * and had never been asked which bucket one landed in.
     */
    [Fact]
    public void The_service_failures_are_recognised()
    {
        Assert.True(ServiceFailures.IsServiceFailure(new Win32Exception()));
        Assert.True(ServiceFailures.IsServiceFailure(new TimeoutException()));
        Assert.True(ServiceFailures.IsServiceFailure(new InvalidOperationException()));

        // Something else entirely: reported as itself, not as the service
        // having misbehaved.
        Assert.False(ServiceFailures.IsServiceFailure(new IOException()));
        Assert.False(ServiceFailures.IsServiceFailure(new UnauthorizedAccessException()));
    }

    /*
     * Narrower, for whether the panel may try at all. A timeout there
     * means something else was slow.
     */
    [Fact]
    public void A_timeout_is_not_a_reason_to_think_the_service_is_broken()
    {
        Assert.False(ServiceFailures.IsServiceControlFailure(new TimeoutException()));

        Assert.True(ServiceFailures.IsServiceControlFailure(new Win32Exception()));
        Assert.True(ServiceFailures.IsServiceControlFailure(new InvalidOperationException()));
    }

    // ---- The port, written once ---------------------------------------

    /*
     * A port nothing can listen on, written six times in the project, one
     * of which quietly dropped an out-of-range value instead of refusing
     * it -- so a row written by one surface could be read by another and
     * disagree about the port.
     */
    [Theory]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(1, true)]
    [InlineData(8080, true)]
    [InlineData(65535, true)]
    [InlineData(65536, false)]
    public void A_port_outside_the_valid_range_is_refused(int port, bool valid)
    {
        Assert.Equal(valid, GatewayConfig.IsValidPort(port));
    }

    /*
     * The lockout, in the unit the person set it in. Rounded up: the box
     * says minutes and truncation of a 30-second block gives zero, which
     * reads as "off" for a limit that is on.
     */
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(59, 1)]
    [InlineData(60, 1)]
    [InlineData(61, 2)]
    [InlineData(300, 5)]
    public void The_lockout_is_shown_in_minutes_and_never_rounds_down(
        int seconds,
        int expectedMinutes)
    {
        Assert.Equal(
            expectedMinutes,
            new GatewayConfig { AuthBlockSeconds = seconds }.LockoutMinutes);
    }

    /*
     * Zero is how the limiter is turned off, and it cannot be written in
     * minutes at all -- which is why the panel disables the box but
     * leaves the value in it.
     */
    [Fact]
    public void An_attempt_limit_of_zero_is_off()
    {
        Assert.False(new GatewayConfig { AuthMaxFailures = 0 }.LockoutIsEnabled);

        Assert.True(new GatewayConfig { AuthMaxFailures = 1 }.LockoutIsEnabled);
    }

    // ---- The direction of a language -----------------------------------

    /*
     * Written as CurrentLanguage == "ar" in eleven places, one of which
     * was choosing the language of the user guide to open. Anything not
     * known to be right-to-left stays left-to-right, which leaves a
     * window usable rather than mirrored.
     */
    [Theory]
    [InlineData("ar", true)]
    [InlineData("AR", true)]
    [InlineData("en", false)]
    [InlineData("fr", false)]
    [InlineData("he", false)]
    [InlineData("", false)]
    public void Only_arabic_lays_the_windows_out_right_to_left(
        string language,
        bool expected)
    {
        try
        {
            Strings.SetLanguage(language);

            Assert.Equal(expected, Strings.IsRightToLeft);
        }
        finally
        {
            Strings.SetLanguage("en");
        }
    }

    private static void Switch(string name, object config) =>
        Set(name, config, changed: true);

    private static void Set(string name, object config, bool changed)
    {
        var property = config.GetType().GetProperty(name)!;

        var current = property.GetValue(config);

        property.SetValue(
            config,
            changed
                ? current switch
                {
                    bool flag => !flag,
                    int number => number + 1,
                    _ => "changed"
                }
                : Defaults[name]);
    }

    /*
     * What each of these starts as, so a setting can be put back after it
     * has been changed once. Guessing the inverse is how a test ends up
     * comparing two settings that both moved.
     */
    private static readonly Dictionary<string, object> Defaults = new()
    {
        [nameof(GatewayConfig.Host)] = "127.0.0.1",
        [nameof(GatewayConfig.Port)] = 8080,
        [nameof(GatewayConfig.MaxRows)] = 1000,
        [nameof(GatewayConfig.CommandTimeoutSeconds)] = 30,
        [nameof(GatewayConfig.AuthMaxFailures)] = 10,
        [nameof(GatewayConfig.AuthWindowSeconds)] = 60,
        [nameof(GatewayConfig.AuthBlockSeconds)] = 60,

        [nameof(OAuthConfig.Enabled)] = false,
        [nameof(OAuthConfig.TeamDomain)] = string.Empty,
        [nameof(OAuthConfig.Audience)] = string.Empty,
        [nameof(OAuthConfig.JwksUri)] = string.Empty,
        [nameof(OAuthConfig.RedirectUri)] = string.Empty,
        [nameof(OAuthConfig.SessionTimeoutMinutes)] = 60,
        [nameof(OAuthConfig.RequireEdgeAccess)] = false
    };
}