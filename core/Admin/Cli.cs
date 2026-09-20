using ByteBridge.Configuration;
using ByteBridge.Data;

namespace ByteBridge.Admin;

/*
 * Administration without a desktop.
 *
 * Windows Server can be installed with no GUI at all, and the control
 * panel is a WPF window, so on Server Core it simply cannot run. Without
 * these commands the service would install and start there and then be
 * impossible to point at a database, which would make the whole
 * no-login story useless in the one place it matters most.
 *
 * Everything here writes the same SQLite file the control panel writes,
 * and the running service picks changes up within a few seconds. There
 * is no need to restart it after any of these.
 */
public static class Cli
{
    public const string Usage = """
        ByteBridge gateway service

        Running with no arguments starts the Windows service. The
        commands below configure it from a terminal, for machines with
        no desktop:

          status                 Show the listener and the databases
          on | off               Whether the gateway should listen
          port <number>          Change the listening port
          key show               Print the API key
          key new                Replace the API key

          db list                List the configured databases
          db enable  <name>      Start answering for a database
          db disable <name>      Stop answering for a database
          db remove  <name>      Forget a database
          db add --name <n> --server <host> --path <file>
                 --user <u> --password <p> [--port <3050>]

          oauth show             Show the Cloudflare Access settings
          oauth set --team-domain <team>.cloudflareaccess.com
                    --audience <AUD tag> --public-hostname <host>
          oauth on | off         Whether Cloudflare Access login is offered

        Changes apply within a few seconds; the service does not need
        restarting.
        """;

    private const string OAuthSetUsage = """
        oauth set --team-domain <team>.cloudflareaccess.com
                  --audience <AUD tag> --public-hostname <host>

        --team-domain      Zero Trust team domain, e.g. my-team.cloudflareaccess.com
        --audience         The Access application's AUD tag (Zero Trust >
                           Access > Applications > Settings)
        --public-hostname  The hostname the tunnel serves, e.g. api.example.com;
                           Access sends visitors back to it after sign-in
        """;

    private const string AddUsage = """
        db add --name <n> --server <host> --path <file>
               --user <u> --password <p> [--port <3050>]

        --port is the database server's port, and defaults to 3050.
        """;

    /*
     * Returns the process exit code, or null when the arguments are not
     * a command at all, which is the signal to run as a service.
     */
    public static int? Run(string[] args, SqliteDatabase? database = null)
    {
        if (args.Length == 0)
        {
            return null;
        }

        if (args[0] is "-h" or "--help" or "/?" or "help")
        {
            Console.WriteLine(Usage);
            return 0;
        }

        try
        {
            return Dispatch(args, database ?? Open());
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("error: " + error.Message);
            return 1;
        }
    }

    /*
     * On a machine with no desktop this can be the first thing to create
     * the settings folder, and it writes a key and passwords into it, so
     * a folder it could not lock down is worth a line on the console
     * rather than silence.
     */
    private static SqliteDatabase Open()
    {
        var database = new SqliteDatabase();

        if (database.PermissionsError != null)
        {
            Console.Error.WriteLine(
                $"warning: could not restrict permissions on {database.DataDirectory}: "
                + database.PermissionsError.Message);
        }

        return database;
    }

    private static int Dispatch(string[] args, SqliteDatabase database) =>
        args[0].ToLowerInvariant() switch
        {
            "status" => Status(database),
            "on" => SetRunning(database, true),
            "off" => SetRunning(database, false),
            "port" => SetPort(database, args),
            "key" => Key(database, args),
            "db" => Db(database, args),
            "oauth" => OAuth(database, args),
            _ => Unknown(args[0])
        };

    private static int Unknown(string verb)
    {
        Console.Error.WriteLine($"error: unknown command '{verb}'.");
        Console.Error.WriteLine();
        Console.Error.WriteLine(Usage);

        return 1;
    }

    private static int Status(SqliteDatabase database)
    {
        var config = database.GetGatewayConfig();
        var connections = database.GetConnections();

        Console.WriteLine($"listening   {(config.AutoStart ? "yes" : "no")}");
        Console.WriteLine($"address     {config.BaseUrl}");
        Console.WriteLine($"api key     {(string.IsNullOrEmpty(config.ApiKey) ? "not set" : "set (run: key show)")}");
        Console.WriteLine($"row cap     {config.MaxRows}");
        Console.WriteLine($"timeout     {config.CommandTimeoutSeconds}s");
        Console.WriteLine();

        if (connections.Count == 0)
        {
            Console.WriteLine("No databases configured. Add one with: db add --help");
            return 0;
        }

        Console.WriteLine("databases");

        foreach (var item in connections)
        {
            Console.WriteLine(
                $"  {(item.Enabled ? "on " : "off")}  {item.Name}  "
                + $"{item.Server}:{item.Port}  {item.Database}");
        }

        return 0;
    }

    private static int SetRunning(SqliteDatabase database, bool running)
    {
        var config = database.GetGatewayConfig();

        config.AutoStart = running;

        database.SaveGatewayConfig(config);

        Console.WriteLine(
            running
                ? $"The gateway will listen on {config.BaseUrl}."
                : "The gateway will stop listening.");

        return 0;
    }

    private static int SetPort(SqliteDatabase database, string[] args)
    {
        if (args.Length < 2
            || !int.TryParse(args[1], out var port)
            || port is < 1 or > 65535)
        {
            Console.Error.WriteLine("error: port takes a number from 1 to 65535.");
            return 1;
        }

        var config = database.GetGatewayConfig();

        config.Port = port;

        database.SaveGatewayConfig(config);

        Console.WriteLine($"The gateway will listen on {config.BaseUrl}.");

        return 0;
    }

    private static int Key(SqliteDatabase database, string[] args)
    {
        var action = args.Length > 1 ? args[1].ToLowerInvariant() : "show";

        switch (action)
        {
            case "show":
                var key = database.GetGatewayConfig().ApiKey;

                if (string.IsNullOrEmpty(key))
                {
                    Console.Error.WriteLine("error: no API key has been generated yet.");
                    return 1;
                }

                Console.WriteLine(key);
                return 0;

            case "new":
                Console.WriteLine(database.RegenerateApiKey());
                Console.Error.WriteLine(
                    "The old key stops working within a few seconds. "
                    + "Update anything that calls the gateway.");
                return 0;

            default:
                Console.Error.WriteLine("error: key takes 'show' or 'new'.");
                return 1;
        }
    }

    private static int OAuth(SqliteDatabase database, string[] args)
    {
        var action = args.Length > 1 ? args[1].ToLowerInvariant() : "show";

        switch (action)
        {
            case "show":
                var config = database.GetOAuthConfig();

                Console.WriteLine($"login         {(config.Enabled ? "on" : "off")}");
                Console.WriteLine($"team domain   {Shown(config.TeamDomain)}");
                Console.WriteLine($"audience      {Shown(config.Audience)}");
                Console.WriteLine($"callback      {Shown(config.RedirectUri)}");
                return 0;

            case "set":
                return SetOAuth(database, args);

            case "on":
                return SetOAuthEnabled(database, true);

            case "off":
                return SetOAuthEnabled(database, false);

            default:
                Console.Error.WriteLine("error: oauth takes 'show', 'set', 'on' or 'off'.");
                return 1;
        }
    }

    private static string Shown(string value) =>
        string.IsNullOrEmpty(value) ? "not set" : value;

    private static int SetOAuth(SqliteDatabase database, string[] args)
    {
        var options = Options(args, 2);

        if (options.ContainsKey("help"))
        {
            Console.WriteLine(OAuthSetUsage);
            return 0;
        }

        string? Required(string name)
        {
            if (options.TryGetValue(name, out var value)
                && !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }

            Console.Error.WriteLine($"error: oauth set needs --{name}.");
            return null;
        }

        var teamDomain = Required("team-domain");
        var audience = Required("audience");
        var publicHostname = Required("public-hostname");

        if (teamDomain == null || audience == null || publicHostname == null)
        {
            return 1;
        }

        /*
         * These are spliced into URLs the gateway redirects browsers to,
         * so anything that is not a bare hostname is refused rather than
         * quietly producing https://https://... .
         */
        foreach (var (label, host) in new[]
                 {
                     ("--team-domain", teamDomain),
                     ("--public-hostname", publicHostname)
                 })
        {
            if (Uri.CheckHostName(host) == UriHostNameType.Unknown)
            {
                Console.Error.WriteLine(
                    $"error: {label} takes a bare hostname such as "
                    + "my-team.cloudflareaccess.com, without https:// or a path.");
                return 1;
            }
        }

        var config = database.GetOAuthConfig();

        config.TeamDomain = teamDomain;
        config.Audience = audience;
        config.JwksUri = $"https://{teamDomain}/cdn-cgi/access/certs";
        config.RedirectUri = $"https://{publicHostname}/auth/callback";

        database.SaveOAuthConfig(config);

        Console.WriteLine(
            config.Enabled
                ? "Cloudflare Access settings saved and applied."
                : "Cloudflare Access settings saved. Turn login on with: oauth on");

        return 0;
    }

    private static int SetOAuthEnabled(SqliteDatabase database, bool enabled)
    {
        var config = database.GetOAuthConfig();

        if (enabled
            && (string.IsNullOrEmpty(config.TeamDomain)
                || string.IsNullOrEmpty(config.Audience)
                || string.IsNullOrEmpty(config.RedirectUri)))
        {
            Console.Error.WriteLine(
                "error: set the team domain, audience and public hostname first "
                + "(oauth set --help).");
            return 1;
        }

        config.Enabled = enabled;

        database.SaveOAuthConfig(config);

        Console.WriteLine(
            enabled
                ? "Cloudflare Access login is on."
                : "Cloudflare Access login is off. The API key still works.");

        return 0;
    }

    private static int Db(SqliteDatabase database, string[] args)
    {
        var action = args.Length > 1 ? args[1].ToLowerInvariant() : "list";

        if (action == "list")
        {
            return Status(database);
        }

        if (action == "add")
        {
            return AddDb(database, args);
        }

        if (args.Length < 3)
        {
            Console.Error.WriteLine($"error: db {action} needs a database name.");
            return 1;
        }

        var found = database.FindConnection(args[2], out var sharedName);

        /*
         * Enabling, disabling or removing whichever of two same-named
         * databases sorted first is a guess, and removing the wrong one
         * is not undoable. The ids tell them apart, and an id is always
         * accepted in place of the name.
         */
        if (sharedName.Count > 1)
        {
            Console.Error.WriteLine(
                $"error: more than one database is called '{args[2]}'. "
                + "Name the one you mean by its id:");

            foreach (var match in sharedName)
            {
                Console.Error.WriteLine(
                    $"  {match.Id}  {match.Server}:{match.Port}  {match.Database}");
            }

            return 1;
        }

        if (found == null)
        {
            Console.Error.WriteLine($"error: no database called '{args[2]}'.");
            return 1;
        }

        switch (action)
        {
            case "enable":
                database.SetEnabled(found.Id, true);
                Console.WriteLine($"{found.Name} is now answering requests.");
                return 0;

            case "disable":
                database.SetEnabled(found.Id, false);
                Console.WriteLine($"{found.Name} will not answer requests.");
                return 0;

            case "remove":
                database.DeleteConnection(found.Id);
                Console.WriteLine($"{found.Name} removed.");
                return 0;

            default:
                Console.Error.WriteLine($"error: unknown db command '{action}'.");
                return 1;
        }
    }

    private static int AddDb(SqliteDatabase database, string[] args)
    {
        var options = Options(args, 2);

        /*
         * "db add --help" is what status suggests when nothing is
         * configured, so it has to answer with how to add one rather
         * than complain that --name is missing.
         */
        if (options.ContainsKey("help"))
        {
            Console.WriteLine(AddUsage);
            return 0;
        }

        string? Required(string name)
        {
            if (options.TryGetValue(name, out var value)
                && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            Console.Error.WriteLine($"error: db add needs --{name}.");
            return null;
        }

        var name = Required("name");
        var server = Required("server");
        var path = Required("path");
        var user = Required("user");
        var password = Required("password");

        if (name == null || server == null || path == null
            || user == null || password == null)
        {
            return 1;
        }

        var port = 3050;

        if (options.TryGetValue("port", out var portText)
            && (!int.TryParse(portText, out port) || port is < 1 or > 65535))
        {
            Console.Error.WriteLine("error: --port takes a number from 1 to 65535.");
            return 1;
        }

        database.AddConnection(new DatabaseConfig
        {
            Name = name,
            Server = server,
            Port = port,
            Username = user,
            Password = password,
            Database = path,
            Enabled = true
        });

        Console.WriteLine($"{name} added and answering requests.");

        return 0;
    }

    /*
     * Reads --key value pairs from the tail of the command line. Values
     * are taken verbatim, so a password containing spaces works when
     * the shell quotes it.
     */
    private static Dictionary<string, string> Options(string[] args, int from)
    {
        var options = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);

        for (var i = from; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var key = args[i][2..];

            if (i + 1 < args.Length
                && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                options[key] = args[++i];
            }
            else
            {
                options[key] = string.Empty;
            }
        }

        return options;
    }
}
