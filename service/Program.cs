using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ByteBridge.Data;
using ByteBridge.Gateway;
using ByteBridge.Admin;
using ByteBridge.Enrollment;
using ByteBridge.Updates;

namespace ByteBridge.Service;

/*
 * One executable, two jobs.
 *
 * With arguments it is an admin tool, for configuring the gateway on a
 * machine with no desktop. With none it is the service itself, which is
 * how the Service Control Manager (or systemd) starts it.
 */
public static class Program
{
    public static int Main(string[] args)
    {
        var handled = Cli.Run(args);

        if (handled != null)
        {
            return handled.Value;
        }

        /*
         * The content root is where the host looks for appsettings.json
         * and watches it for changes, and it defaults to the working
         * directory. systemd starts a service in /, and the watcher
         * walks everything under its root: on a real machine that is the
         * whole disk, which keeps start-up busy long past systemd's
         * timeout and the service never reports ready. Anchor it to the
         * folder the program lives in, which is small and ours.
         */
        var builder = Host.CreateApplicationBuilder(
            new HostApplicationBuilderSettings
            {
                Args = args,
                ContentRootPath = AppContext.BaseDirectory,
            });

        /*
         * Makes this a real service: the SCM is told when startup has
         * finished, stop requests arrive as cancellation, and log
         * messages go to the Windows event log, which is the only place
         * anyone can read them when nobody is signed in.
         */
        if (OperatingSystem.IsWindows())
        {
            builder.Services.AddWindowsService(options =>
                options.ServiceName = "ByteBridge");

            builder.Logging.AddEventLog(settings =>
            {
                if (OperatingSystem.IsWindows())
                {
                    settings.SourceName = "ByteBridge";
                }
            });
        }
        else
        {
            /*
             * Under systemd the unit's stdout is the journal, so the
             * console logger is all that is needed; this adds the
             * ready notification and the priority prefixes journalctl
             * understands. Outside systemd it does nothing.
             */
            builder.Services.AddSystemd();
        }

        /*
         * One SqliteDatabase and one GatewayServer for the lifetime of
         * the process. SqliteDatabase opens a connection per call, so
         * sharing it costs nothing and keeps the migration and schema
         * setup to a single run at startup.
         */
        builder.Services.AddSingleton(provider =>
        {
            /*
             * SqliteDatabase locks its folder down itself, which is what
             * covers the control panel and the command line as well. The
             * service is where a failure to do so can still be read
             * afterwards, so it is sent to the event log from here.
             */
            var database = new SqliteDatabase();

            if (database.PermissionsError != null)
            {
                provider.GetRequiredService<ILogger<GatewayWorker>>().LogWarning(
                    database.PermissionsError,
                    "Could not restrict permissions on {Folder}. "
                    + "It may be readable by other accounts on this machine.",
                    database.DataDirectory);
            }

            return database;
        });

        builder.Services.AddSingleton(provider =>
        {
            var database = provider.GetRequiredService<SqliteDatabase>();

            return new GatewayServer(
                database,
                new RequestLog(database.LogDirectory));
        });

        builder.Services.AddSingleton(provider =>
            new ConnectionHealthMonitor(
                provider.GetRequiredService<SqliteDatabase>()));

        /*
         * Looks for a newer release once a day and records it; it never
         * installs one. See UpdateWorker.
         */
        builder.Services.AddSingleton(provider =>
        {
            var current = UpdateService.CurrentVersion();

            return new UpdateService(
                provider.GetRequiredService<SqliteDatabase>(),
                new UpdateChecker(UpdateChecker.CreateHttpClient(), current),
                new SystemClock(),
                current);
        });

        builder.Services.AddHostedService<UpdateWorker>();

        builder.Services.AddHostedService<GatewayWorker>();

        builder.Build().Run();

        return 0;
    }
}
