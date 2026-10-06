using ByteBridge.Enrollment;
using Xunit;

namespace ByteBridge.Tests;

/*
 * The address box, asked the same question the CLI asks.
 *
 * The control panel asked Contains('@'), so "a@" passed it -- and then
 * EnrollOptionsFor threw EnrollmentException from inside an async void
 * handler, outside the try that catches it. An unhandled exception on
 * the dispatcher, and the app's own handler for those says ByteBridge hit
 * an unexpected error and needs to close. Typing an address that was not
 * finished yet and pressing the button closed the control panel.
 *
 * That is the whole of it: a private handler on a window, in a project
 * the suite cannot reference, wrapping one call that was outside the one
 * handler that would have caught it. Nothing about it was reachable by a
 * test, and nothing about it was obvious to read.
 *
 * So the rule is the CLI's rule, asked before the call instead of thrown
 * after it. Anything that would throw is false here.
 */
public class EnrollmentAddressTests
{
    [Theory]
    [InlineData("me@example.com")]
    [InlineData("me.name+tag@sub.example.co.uk")]
    [InlineData("m@e.io")]
    public void An_address_worth_attempting_is_accepted(string email)
    {
        Assert.True(EnrollmentCommands.IsAcceptableEmail(email));
    }

    [Theory]
    // The ones that pass Contains('@') and are thrown by ParseEnroll.
    [InlineData("a@")]
    [InlineData("@b")]
    [InlineData("a@b")]
    [InlineData("a@b.c")]
    [InlineData("a b@c")]
    [InlineData("me@example")]
    [InlineData("me at example.com")]
    [InlineData("@")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Anything_that_would_throw_is_refused_in_advance(string? email)
    {
        Assert.False(EnrollmentCommands.IsAcceptableEmail(email));
    }

    /*
     * The box trims before asking, so whitespace round a real address is
     * not a reason to refuse it -- and the rule has to agree with the
     * CLI's, or the panel rejects what the command line accepts.
     */
    [Fact]
    public void Surrounding_whitespace_is_not_a_reason_to_refuse()
    {
        Assert.True(EnrollmentCommands.IsAcceptableEmail("  me@example.com  "));
    }
}