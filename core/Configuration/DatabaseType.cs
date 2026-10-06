namespace ByteBridge.Configuration;

/*
 * Which database engine a connection talks to. Stored by name, so the
 * numbers can be reordered without touching anyone's settings file.
 *
 * SQL Server was in this enum and has been taken back out. A value left
 * behind by that version -- "SqlServer" -- parses to Firebird, which is
 * what TryParse's default already does, so a settings file written then
 * cannot make the gateway open an engine it cannot serve. See
 * SqlProviders for the reasoning.
 */
public enum DatabaseType
{
    Firebird,
    PostgreSql
}

public static class DatabaseTypes
{
    public static int DefaultPort(this DatabaseType type) => type switch
    {
        DatabaseType.PostgreSql => 5432,
        _ => 3050
    };

    public static string DefaultUser(this DatabaseType type) => type switch
    {
        DatabaseType.PostgreSql => "postgres",
        _ => "SYSDBA"
    };

    public static string DisplayName(this DatabaseType type) => type switch
    {
        DatabaseType.PostgreSql => "PostgreSQL",
        _ => "Firebird"
    };

    /*
     * What the Database field holds: a file for Firebird, a name for the
     * others. Used to word prompts and errors.
     */
    public static string DatabaseFieldName(this DatabaseType type) => type switch
    {
        DatabaseType.Firebird => "database path",
        _ => "database name"
    };

    public static bool TryParse(string? text, out DatabaseType type)
    {
        switch (text?.Trim().ToLowerInvariant().Replace(" ", string.Empty).Replace("-", string.Empty))
        {
            case "firebird" or "fb":
                type = DatabaseType.Firebird;
                return true;

            case "postgresql" or "postgres" or "pg":
                type = DatabaseType.PostgreSql;
                return true;

            default:
                type = DatabaseType.Firebird;
                return false;
        }
    }
}
