using Xunit;
using ByteBridge.Configuration;
using ByteBridge.Localization;

namespace ByteBridge.Tests;

/*
 * Two decisions that used to live in the add/edit wizard, where no
 * test could reach them and both were wrong in review: the save gate
 * that refuses an unsupported engine, and the localisation fallback
 * whose condition was inverted, so the Arabic string it fetched was
 * discarded before it was ever shown.
 *
 * Both now live where the suite can call them -- NeedsEngineChoice on
 * DatabaseConfig, GetOrDefault beside the strings -- and these are the
 * tests that were not possible before.
 */
/*
 * The one collection for the tests that change the current language.
 *
 * Strings.SetLanguage is static, and xUnit runs different test classes
 * at the same time, so two classes that each set the language -- and
 * each read it back in its own finally -- are reading and writing the
 * same field over each other's heads. The failure is a test asserting
 * Arabic and getting English, which reads as a translation bug and is
 * nothing of the kind.
 *
 * The collection is what makes them take turns. Same reason the console
 * redirection tests share one.
 */
[Collection("Language")]
public class WizardDecisionTests
{
    [Fact]
    public void An_unsupported_row_waits_for_an_engine_before_it_can_be_saved()
    {
        var unsupported = Unsupported();

        // Nothing chosen: the only state that blocks.
        Assert.True(unsupported.NeedsEngineChoice(engineChosen: false));

        // Chosen, and the row is repaired -- which is the way out.
        Assert.False(unsupported.NeedsEngineChoice(engineChosen: true));
    }

    [Fact]
    public void A_supported_row_never_waits_for_an_engine()
    {
        var firebird = Firebird();
        var postgres = Postgres();

        // Whatever the box says, including nothing selected: a
        // Firebird connection opens with its engine already chosen, so
        // an unselected box must not block a row that never needed one.
        Assert.False(firebird.NeedsEngineChoice(engineChosen: false));
        Assert.False(firebird.NeedsEngineChoice(engineChosen: true));
        Assert.False(postgres.NeedsEngineChoice(engineChosen: false));
    }

    [Fact]
    public void A_translation_is_never_replaced_by_the_English_fallback()
    {
        try
        {
            Strings.SetLanguage("ar");

            var arabic = Strings.GetOrDefault(
                "WizardEngineUnsupported",
                "english fallback");

            // The bug: this came back as the fallback, because the test
            // was inverted and any non-blank value was overwritten.
            Assert.NotEqual("english fallback", arabic);
            Assert.Contains("قاعدة", arabic);
        }
        finally
        {
            Strings.SetLanguage("en");
        }
    }

    /*
     * A language the tables have no entry for at all, so the English
     * table is the only place the key can come from.
     *
     * Which is the third case, and the only one reachable here: both
     * tables carry all 155 keys, so "this language has no entry for
     * this key" cannot be produced by choosing a different key. The
     * earlier version of this test claimed to cover it with
     * WizardTestSucceeded -- which has an Arabic entry -- and so passed
     * without ever reaching the branch.
     *
     * A genuinely missing key in a present language needs a table that
     * lags, which is a condition this project does not have and would
     * rather not create to satisfy a test.
     */
    [Fact]
    public void A_language_the_tables_do_not_have_comes_from_English()
    {
        try
        {
            Strings.SetLanguage("xx");

            var key = Strings.FindKeyWhoseEnglishTextIsItsOwnName();

            Assert.NotNull(key);

            // Nothing for "xx" anywhere, so this is the English value
            // and the caller's text is not consulted at all.
            Assert.Equal(
                key,
                Strings.GetOrDefault(key!, "the caller's own text"));
        }
        finally
        {
            Strings.SetLanguage("en");
        }
    }

    /*
     * A key the current language does have, so the other branch is the
     * one taken. Without it, the English-fallback test above would pass
     * on a code path that never consults the current language at all.
     */
    [Fact]
    public void A_key_the_current_language_does_have_comes_from_it()
    {
        try
        {
            Strings.SetLanguage("ar");

            Assert.Equal(
                "نجح الاتصال.",
                Strings.GetOrDefault(
                    "WizardTestSucceeded",
                    "the caller's text"));
        }
        finally
        {
            Strings.SetLanguage("en");
        }
    }

    /*
     * Twelve entries are their own translation -- "Port" is "Port",
     * "Cancel" is "Cancel" -- so spotting a missing key by comparing
     * the returned text with the key threw twelve good translations
     * away and handed the caller the fallback instead. The key is asked
     * of the tables now, which has an answer either way.
     *
     * The key is found rather than named: writing "Port" here would
     * break the day somebody translates it, and a test should not fail
     * for a reason it is not about.
     */
    [Fact]
    public void A_translation_that_happens_to_equal_its_key_is_still_a_translation()
    {
        var key = Strings.FindKeyWhoseEnglishTextIsItsOwnName();

        Assert.NotNull(key);

        Assert.Equal(key, Strings.Get(key!));

        Assert.Equal(
            key,
            Strings.GetOrDefault(key!, "the caller's own words"));
    }

    [Fact]
    public void A_key_the_tables_do_not_have_uses_the_callers_text()
    {
        var value = Strings.GetOrDefault(
            "NoSuchKeyAnywhere",
            "the caller's own words");

        Assert.Equal("the caller's own words", value);
    }

    /*
     * The caller's text, when the caller supplied none.
     *
     * The reason this method exists is that a person is told something
     * rather than shown an empty label, so handing back the blank it
     * was given defeats it -- and does so quietly, since "" is a
     * perfectly ordinary string. What Get would have shown is the
     * least bad answer left.
     *
     * No key involved: the tables are never consulted on this path,
     * and the value is a literal either way.
     */
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_caller_text_shows_the_key_rather_than_nothing(string fallback)
    {
        Assert.Equal(
            "NoSuchKeyAnywhere",
            Strings.GetOrDefault("NoSuchKeyAnywhere", fallback));
    }

    [Fact]
    public void The_gate_and_the_fallback_are_not_guesses()
    {
        /*
         * Both bugs here were invisible because nothing executed them.
         * This is the assertion that would have caught the second: the
         * string the window shows is the one in the table, in the
         * current language, with the fallback only when there is none.
         */
        foreach (var language in new[] { "en", "ar" })
        {
            try
            {
                Strings.SetLanguage(language);

                var shown = Strings.GetOrDefault(
                    "WizardEngineUnsupported",
                    "fallback");

                Assert.Equal(
                    Strings.Get("WizardEngineUnsupported"),
                    shown);

                Assert.False(string.IsNullOrWhiteSpace(shown));
            }
            finally
            {
                Strings.SetLanguage("en");
            }
        }
    }

    private static DatabaseConfig Firebird() =>
        new()
        {
            Name = "Sales",
            Type = DatabaseType.Firebird,
            Server = "localhost",
            Port = 3050,
            Username = "SYSDBA",
            Password = "p",
            Database = "/data/sales.fdb"
        };

    private static DatabaseConfig Postgres() =>
        new()
        {
            Name = "Shop",
            Type = DatabaseType.PostgreSql,
            Server = "localhost",
            Port = 5432,
            Username = "postgres",
            Password = "p",
            Database = "shop"
        };

    private static DatabaseConfig Unsupported()
    {
        var config = Postgres();

        // What SqliteDatabase returns for a row naming an engine this
        // build has no provider for.
        config.EngineIsSupported = false;

        return config;
    }
}