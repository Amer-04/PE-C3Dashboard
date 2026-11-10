using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NJTruckCharging.Application.Abstractions;
using NJTruckCharging.Application.Import;
using NJTruckCharging.Infrastructure.Persistence;

namespace NJTruckCharging.Infrastructure;

public static class DatabaseInitializer
{
    /// <summary>
    /// Applies migrations and, when the database is empty, imports the workbook configured at
    /// <c>Import:WorkbookPath</c> (relative paths are resolved from the API's content root).
    /// </summary>
    public static async Task InitializeDatabaseAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseInitializer");
        var config = sp.GetRequiredService<IConfiguration>();
        var db = sp.GetRequiredService<AppDbContext>();

        if (config.GetValue<bool>("Database:SkipInitialization"))
        {
            // Read-only deployments (e.g. the cloud demo, whose database login can only SELECT): never migrate, seed
            // or create anything — the schema and data were prepared in advance. Only load the active methodology.
            logger.LogInformation("Database:SkipInitialization is set: no migrations, seeding or baseline creation.");
            await sp.GetRequiredService<NJTruckCharging.Application.Methodology.MethodologyService>().RefreshCacheAsync(ct);
            return;
        }
        if (db.Database.IsPostgres())
            throw new InvalidOperationException(
                "PostgreSQL databases are prepared with the NJTruckCharging.CloudCopy tool; set Database:SkipInitialization=true.");

        if (db.Database.GetMigrations().Any())
        {
            var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
            var refusal = MigrationGuard.Check(db.Database.GetDbConnection().Database, pending, config);
            if (refusal is not null)
            {
                logger.LogCritical("{Reason}", refusal);
                throw new InvalidOperationException(refusal);
            }
            await db.Database.MigrateAsync(ct);
        }
        else
            await db.Database.EnsureCreatedAsync(ct);

        var methodology = sp.GetRequiredService<NJTruckCharging.Application.Methodology.MethodologyService>();
        if (await sp.GetRequiredService<IWriteStore>().HasSitesAsync(ct))
        {
            // Creates baseline Version 1 on first run (after verifying it reproduces the stored scores) and loads the
            // active methodology into the cache.
            await methodology.EnsureInitializedAsync(ct);
            return;
        }

        var path = config["Import:WorkbookPath"];
        if (string.IsNullOrWhiteSpace(path))
        {
            logger.LogWarning("Database is empty and Import:WorkbookPath is not set. POST the workbook to /api/admin/import.");
            await methodology.EnsureInitializedAsync(ct);
            return;
        }

        var env = sp.GetRequiredService<IHostEnvironment>();
        var full = Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(env.ContentRootPath, path));
        if (!File.Exists(full))
        {
            logger.LogWarning("Workbook not found at {Path}. POST the workbook to /api/admin/import.", full);
            await methodology.EnsureInitializedAsync(ct);
            return;
        }

        logger.LogInformation("Seeding database from {Path} …", full);
        await using var stream = File.OpenRead(full);
        await sp.GetRequiredService<ImportService>().ImportAsync(stream, ct);
    }
}
