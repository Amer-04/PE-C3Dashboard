using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;

namespace NJTruckCharging.Infrastructure;

/// <summary>
/// Which database engine the app talks to. SQL Server (LocalDB) is the default for local/office use;
/// <c>Database:Provider = Postgres</c> selects PostgreSQL (the Supabase cloud deployment).
/// </summary>
public static class DatabaseProvider
{
    public const string SqlServerName = "Microsoft.EntityFrameworkCore.SqlServer";
    public const string NpgsqlName = "Npgsql.EntityFrameworkCore.PostgreSQL";

    /// <summary>The PostgreSQL schema that holds the app's tables (kept out of Supabase's API-exposed "public" schema).</summary>
    public const string PostgresSchema = "c3";

    public static bool IsPostgres(IConfiguration configuration) =>
        string.Equals(configuration["Database:Provider"], "Postgres", StringComparison.OrdinalIgnoreCase)
        || string.Equals(configuration["Database:Provider"], "PostgreSQL", StringComparison.OrdinalIgnoreCase);

    public static bool IsSqlServer(this DatabaseFacade db) => db.ProviderName == SqlServerName;
    public static bool IsPostgres(this DatabaseFacade db) => db.ProviderName == NpgsqlName;
}
