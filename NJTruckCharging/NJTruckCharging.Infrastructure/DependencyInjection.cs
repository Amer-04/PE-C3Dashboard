using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NJTruckCharging.Application.Abstractions;
using NJTruckCharging.Infrastructure.Excel;
using NJTruckCharging.Infrastructure.Persistence;

namespace NJTruckCharging.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// DI key of the database context used ONLY by <see cref="MethodologyStore"/>. With
    /// <c>ConnectionStrings:MethodologyWriter</c> set it connects as the restricted methodology writer role (cloud: may
    /// change methodology tables and the score columns of CandidateSites, nothing else); otherwise it is the normal context.
    /// </summary>
    public const string MethodologyWriterKey = "methodology-writer";
    public const string MethodologyWriterConnectionName = "MethodologyWriter";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is missing.");
        var postgres = DatabaseProvider.IsPostgres(configuration);
        if (postgres)
        {
            // Cloud deployment (Supabase PostgreSQL). DateTime values keep their SQL Server meaning (UTC, no offset).
            AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        }

        services.AddDbContext<AppDbContext>(o => Configure(o, connectionString, postgres));

        var writer = configuration.GetConnectionString(MethodologyWriterConnectionName);
        if (string.IsNullOrWhiteSpace(writer))
        {
            services.AddKeyedScoped<AppDbContext>(MethodologyWriterKey, (sp, _) => sp.GetRequiredService<AppDbContext>());
        }
        else
        {
            services.AddKeyedScoped<AppDbContext>(MethodologyWriterKey, (sp, _) =>
            {
                var b = new DbContextOptionsBuilder<AppDbContext>();
                Configure(b, writer, postgres);
                b.UseLoggerFactory(sp.GetRequiredService<ILoggerFactory>());
                return new AppDbContext(b.Options);
            });
        }

        services.AddScoped<IReadStore, ReadStore>();
        services.AddScoped<IWriteStore, WriteStore>();
        services.AddScoped<ISavedSiteStore, SavedSiteStore>();
        services.AddScoped<ISiteContactStore, SiteContactStore>();
        services.AddScoped<IFeasibilityStore, FeasibilityStore>();
        services.AddScoped<IScreeningStore, ScreeningStore>();
        services.AddScoped<IResearchHistoryStore, ResearchHistoryStore>();
        services.AddScoped<IMethodologyStore, MethodologyStore>();
        services.AddScoped<ICustomFieldStore, CustomFieldStore>();
        services.AddSingleton<IActivationHook, NoOpActivationHook>();
        services.AddSingleton<IWorkbookReader, CandidateWorkbookReader>();
        return services;
    }

    private static void Configure(DbContextOptionsBuilder o, string connectionString, bool postgres)
    {
        if (postgres)
        {
            // Unqualified table names in the few raw-SQL statements resolve to the app's schema.
            var pg = new Npgsql.NpgsqlConnectionStringBuilder(connectionString);
            if (string.IsNullOrEmpty(pg.SearchPath)) pg.SearchPath = DatabaseProvider.PostgresSchema;
            o.UseNpgsql(pg.ConnectionString, x =>
            {
                x.EnableRetryOnFailure(3);
                x.CommandTimeout(180);
                x.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
            });
        }
        else
        {
            o.UseSqlServer(connectionString, sql =>
            {
                sql.EnableRetryOnFailure(3);
                sql.CommandTimeout(180);
                sql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
            });
        }
    }
}
