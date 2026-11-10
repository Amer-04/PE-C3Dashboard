using Microsoft.Extensions.Configuration;

namespace NJTruckCharging.Infrastructure;

/// <summary>
/// Stops startup from silently applying EF Core migrations to a protected database (the live
/// <c>NJTruckCharging</c> data by default). When the target database is protected and has pending migrations,
/// startup fails with an explanation instead of migrating. Applying them is a deliberate, separate step:
/// set <c>Database:AllowProtectedMigration=true</c> for that one run.
/// Databases that are not protected (demo, test) migrate normally; a protected database with nothing pending
/// starts normally.
/// </summary>
public static class MigrationGuard
{
    public static readonly string[] DefaultProtectedNames = ["NJTruckCharging"];

    public static IReadOnlyCollection<string> ProtectedNames(IConfiguration config)
    {
        var configured = config.GetSection("Database:ProtectedNames").Get<string[]>();
        return configured is { Length: > 0 } ? configured : DefaultProtectedNames;
    }

    /// <summary>Null when migrating is allowed; otherwise the reason it is refused.</summary>
    public static string? Check(string? databaseName, IReadOnlyCollection<string> pendingMigrations, IConfiguration config)
    {
        if (pendingMigrations.Count == 0 || string.IsNullOrEmpty(databaseName)) return null;
        if (!ProtectedNames(config).Contains(databaseName, StringComparer.OrdinalIgnoreCase)) return null;
        if (config.GetValue<bool>("Database:AllowProtectedMigration")) return null;
        return $"Refusing to apply {pendingMigrations.Count} pending migration(s) ({string.Join(", ", pendingMigrations)}) " +
               $"to protected database '{databaseName}'. Point ConnectionStrings:DefaultConnection at a demo/test database, " +
               "or, once approved, run once with Database:AllowProtectedMigration=true.";
    }
}
