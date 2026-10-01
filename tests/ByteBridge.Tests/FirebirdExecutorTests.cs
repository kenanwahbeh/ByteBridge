using Xunit;
using ByteBridge.Gateway;

namespace ByteBridge.Tests;

/*
 * The read-only guard, on its own.
 *
 * It decides whether a statement may go to /query, and it is the only
 * thing standing between a read endpoint and an accidental write, so
 * it is worth testing away from any server.
 */
public class FirebirdExecutorTests
{
    [Theory]
    [InlineData("SELECT 1 FROM RDB$DATABASE")]
    [InlineData("select 1 from rdb$database")]
    [InlineData("  \t\r\n SELECT 1 FROM RDB$DATABASE")]
    [InlineData("-- a comment\nSELECT 1 FROM RDB$DATABASE")]
    [InlineData("--one\n--two\nSELECT 1 FROM RDB$DATABASE")]
    [InlineData("/* a block */ SELECT 1 FROM RDB$DATABASE")]
    [InlineData("/* multi\nline */\nSELECT 1 FROM RDB$DATABASE")]
    [InlineData("/*a*/ -- b\n /*c*/ SELECT 1 FROM RDB$DATABASE")]
    [InlineData("WITH T AS (SELECT 1 X FROM RDB$DATABASE) SELECT X FROM T")]
    [InlineData("SELECT\n  ID\nFROM CUSTOMERS")]
    [InlineData("SELECT(1)")]
    public void Reads_are_allowed(string sql)
    {
        Assert.True(FirebirdExecutor.IsReadOnlyStatement(sql));
    }

    [Theory]
    [InlineData("SELECT GEN_ID(G_CUSTOMERS, 1) FROM RDB$DATABASE")]
    [InlineData("select gen_id(g_customers,-1) from rdb$database")]
    [InlineData("SELECT GEN_ID ( \"G_Customers\" , 10 ) FROM RDB$DATABASE")]
    [InlineData("SELECT GEN_ID(G, @step) FROM RDB$DATABASE")]
    [InlineData("SELECT GEN_ID(G, 0 + 1) FROM RDB$DATABASE")]
    [InlineData("SELECT NEXT VALUE FOR G_CUSTOMERS FROM RDB$DATABASE")]
    [InlineData("SELECT next\r\n value  for G FROM RDB$DATABASE")]
    [InlineData("SELECT /* peek */ GEN_ID(G, 0), GEN_ID(G, 1) FROM RDB$DATABASE")]
    [InlineData("SELECT GEN_ID /* hide */ (G, 1) FROM RDB$DATABASE")]
    public void A_statement_that_moves_a_generator_is_noticed(string sql)
    {
        Assert.True(FirebirdExecutor.AdvancesSequence(sql));
    }

    [Theory]
    [InlineData("SELECT GEN_ID(G_CUSTOMERS, 0) FROM RDB$DATABASE")]
    [InlineData("SELECT GEN_ID( \"G_Customers\" ,0 ) FROM RDB$DATABASE")]
    [InlineData("SELECT 'GEN_ID(G, 1)' FROM RDB$DATABASE")]
    [InlineData("SELECT 1 FROM RDB$DATABASE -- GEN_ID(G, 1)")]
    [InlineData("SELECT 1 /* NEXT VALUE FOR G */ FROM RDB$DATABASE")]
    [InlineData("SELECT 'it''s GEN_ID(G, 1)' FROM RDB$DATABASE")]
    [InlineData("SELECT ID, NAME FROM CUSTOMERS")]
    public void Peeking_at_a_generator_or_mentioning_one_is_not_moving_it(string sql)
    {
        Assert.False(FirebirdExecutor.AdvancesSequence(sql));
    }

    [Theory]
    [InlineData("DELETE FROM CUSTOMERS")]
    [InlineData("UPDATE CUSTOMERS SET NAME = 'x'")]
    [InlineData("INSERT INTO CUSTOMERS (ID) VALUES (1)")]
    [InlineData("DROP TABLE CUSTOMERS")]
    [InlineData("CREATE TABLE T (ID INT)")]
    [InlineData("ALTER TABLE CUSTOMERS ADD X INT")]
    [InlineData("EXECUTE PROCEDURE WIPE")]
    [InlineData("EXECUTE BLOCK AS BEGIN DELETE FROM CUSTOMERS; END")]
    [InlineData("MERGE INTO CUSTOMERS USING T ON (1=1) WHEN MATCHED THEN DELETE")]
    [InlineData("-- looks harmless\nDELETE FROM CUSTOMERS")]
    [InlineData("/* hidden */ UPDATE CUSTOMERS SET NAME = 'x'")]
    public void Writes_are_refused(string sql)
    {
        Assert.False(FirebirdExecutor.IsReadOnlyStatement(sql));
    }

    [Theory]
    // A keyword must end at a word boundary, not merely start the text.
    [InlineData("SELECTIVE_PROCEDURE()")]
    [InlineData("SELECT_ALL()")]
    [InlineData("WITHDRAW FROM ACCOUNTS")]
    [InlineData("WITHOUT_X()")]
    public void A_word_that_merely_starts_with_a_keyword_is_not_a_read(string sql)
    {
        Assert.False(FirebirdExecutor.IsReadOnlyStatement(sql));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("-- only a comment")]
    [InlineData("/* only a block comment */")]
    // An unterminated block comment must not be read as a statement.
    [InlineData("/* never closed SELECT 1")]
    public void Nothing_to_run_is_not_a_read(string sql)
    {
        Assert.False(FirebirdExecutor.IsReadOnlyStatement(sql));
    }

    [Fact]
    public void A_connection_string_carries_the_connection_details()
    {
        var built = FirebirdExecutor.BuildConnectionString(
            new Configuration.DatabaseConfig
            {
                Server = "db.example.com",
                Port = 3051,
                Username = "SYSDBA",
                Password = "secret",
                Database = "/data/sales.fdb"
            });

        Assert.Contains("db.example.com", built);
        Assert.Contains("3051", built);
        Assert.Contains("/data/sales.fdb", built);
        Assert.Contains("UTF8", built);
    }
}
