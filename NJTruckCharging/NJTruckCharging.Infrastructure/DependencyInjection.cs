using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NJTruckCharging.Application.Abstractions;
using NJTruckCharging.Infrastructure.Excel;
using NJTruckCharging.Infrastructure.Persistence;

namespace NJTruckCharging.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is missing.");

        if (DatabaseProvider.IsPostgres(configuration))
        {
            // Cloud deployment (Supabase PostgreSQL). DateTime values keep their SQL Server meaning (UTC, no offset).
            AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
            // Unqualified table names in the few raw-SQL statements resolve to the app's schema.
            var pgConnection = new Npgsql.NpgsqlConnectionStringBuilder(connectionString);
            if (string.IsNullOrEmpty(pgConnection.SearchPath)) pgConnection.SearchPath = DatabaseProvider.PostgresSchema;
            services.AddDbContext<AppDbContext>(o => o.UseNpgsql(pgConnection.ConnectionString, pg =>
            {
                pg.EnableRetryOnFailure(3);
                pg.CommandTimeout(180);
                pg.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
            }));
        }
        else
        {
            services.AddDbContext<AppDbContext>(o => o.UseSqlServer(connectionString, sql =>
            {
                sql.EnableRetryOnFailure(3);
                sql.CommandTimeout(180);
                sql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
            }));
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
}
