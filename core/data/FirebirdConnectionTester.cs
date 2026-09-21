using FirebirdSql.Data.FirebirdClient;
using ByteBridge.Configuration;

namespace ByteBridge.Data;

/*
 * Whether a database's connection details actually work, before they
 * are saved. Public (unlike FirebirdExecutor, which the gateway keeps
 * to itself) because both the desktop window's "turn online" flow and
 * the add/edit wizard need it, and duplicating a connection-string
 * builder in two places is how they'd quietly drift apart.
 */
public static class FirebirdConnectionTester
{
    public static async Task<(bool Succeeded, string? Error)> TestAsync(
        DatabaseConfig config,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var builder = new FbConnectionStringBuilder
            {
                DataSource = config.Server,
                Port = config.Port,
                Database = config.Database,
                UserID = config.Username,
                Password = config.Password,
                Charset = "UTF8",
                ConnectionTimeout = 10
            };

            await using var connection = new FbConnection(builder.ToString());

            await connection.OpenAsync(cancellationToken);

            await connection.CloseAsync();

            return (true, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            /*
             * Being told to stop is not a failed test: reporting it as
             * one would mark a working database offline every time the
             * service shuts down mid-probe.
             */
            throw;
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
