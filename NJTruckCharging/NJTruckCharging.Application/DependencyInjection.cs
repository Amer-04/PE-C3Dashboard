using Microsoft.Extensions.DependencyInjection;
using NJTruckCharging.Application.Dashboard;
using NJTruckCharging.Application.Import;
using NJTruckCharging.Application.Reference;
using NJTruckCharging.Application.Sites;

namespace NJTruckCharging.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<SiteQueryService>();
        services.AddScoped<DashboardService>();
        services.AddScoped<ReferenceService>();
        services.AddScoped<ImportService>();
        services.AddScoped<SavedSites.SavedSiteService>();
        services.AddScoped<Contacts.ContactService>();
        services.AddScoped<Feasibility.FeasibilityService>();
        services.AddScoped<Comparison.SiteComparisonService>();
        services.AddScoped<Screening.ScreeningService>();
        services.AddScoped<Methodology.MethodologyService>();
        services.AddScoped<Methodology.CustomFieldService>();
        services.AddSingleton<Methodology.ActiveMethodologyCache>();
        services.AddSingleton<Methodology.FieldStatsCache>();
        return services;
    }
}
