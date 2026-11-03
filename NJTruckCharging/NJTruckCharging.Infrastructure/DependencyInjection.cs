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

        services.AddDbContext<AppDbContext>(o => o.UseSqlServer(connectionString, sql =>
        {
            sql.EnableRetryOnFailure(3);
            sql.CommandTimeout(180);
            sql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
        }));

        services.AddScoped<IReadStore, ReadStore>();
        services.AddScoped<IWriteStore, WriteStore>();
        services.AddScoped<ISavedSiteStore, SavedSiteStore>();
        services.AddScoped<ISiteContactStore, SiteContactStore>();
        services.AddScoped<IFeasibilityStore, FeasibilityStore>();
        services.AddScoped<IScreeningStore, ScreeningStore>();
        services.AddSingleton<IWorkbookReader, CandidateWorkbookReader>();
        return services;
    }
}
