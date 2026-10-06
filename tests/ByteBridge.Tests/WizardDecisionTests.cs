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

    [Fact]
    public void A_missing_language_falls_back_to_English_rather_than_the_callers_text()
    {
        try
        {
            Strings.SetLanguage("ar");

            // A key with no Arabic entry falls back inside Get, so the
            // caller's text is only for a build whose tables lack the
            // key entirely.
            var value = Strings.GetOrDefault(
                "WizardTestSucceeded",
                "english fallback");

            Assert.Contains("نجح", value);
        }
        finally
        {
            Strings.SetLanguage("en");
        }
    }

    [Fact]
    public void A_key_the_tables_do_not_have_uses_the_callers_text()
    {
        var value = Strings.GetOrDefault(
            "NoSuchKeyAnywhere",
            "the caller's own words");

        Assert.Equal("the caller's own words", value);
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