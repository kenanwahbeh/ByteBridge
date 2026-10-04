namespace ByteBridge.Configuration;

/*
 * Which database engine a connection talks to. Stored by name, so the
 * numbers can be reordered without touching anyone's settings file.
 */
public enum DatabaseType
{
    Firebird,
    PostgreSql,
    SqlServer
}

public static class DatabaseTypes
{
    public static int DefaultPort(this DatabaseType type) => type switch
    {
        DatabaseType.PostgreSql => 5432,
        DatabaseType.SqlServer => 1433,
        _ => 3050
    };

    public static string DefaultUser(this DatabaseType type) => type switch
    {
        DatabaseType.PostgreSql => "postgres",
        DatabaseType.SqlServer => "sa",
        _ => "SYSDBA"
    };

    public static string DisplayName(this DatabaseType type) => type switch
    {
        DatabaseType.PostgreSql => "PostgreSQL",
        DatabaseType.SqlServer => "SQL Server",
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

            case "sqlserver" or "mssql":
                type = DatabaseType.SqlServer;
                return true;

            default:
                type = DatabaseType.Firebird;
                return false;
        }
    }
}
