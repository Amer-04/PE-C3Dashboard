using NJTruckCharging.Domain.Entities;

namespace NJTruckCharging.Application.Abstractions;

/// <summary>
/// Read-only query surface over the database. Application services compose LINQ queries;
/// Infrastructure executes them (EF Core), so this layer has no EF dependency.
/// </summary>
public interface IReadStore
{
    IQueryable<CandidateSite> Sites { get; }
    IQueryable<NearbyFleet> NearbyFleets { get; }
    IQueryable<SegmentInfo> Segments { get; }
    IQueryable<PowerEdisonSite> PowerEdisonSites { get; }
    IQueryable<Interchange> Interchanges { get; }
    IQueryable<MethodologyNote> MethodologyNotes { get; }
    IQueryable<SavedSite> SavedSites { get; }
    IQueryable<SiteContact> SiteContacts { get; }
    IQueryable<FeasibilityItem> FeasibilityItems { get; }
    IQueryable<SiteScreening> SiteScreenings { get; }
    IQueryable<SiteScreeningEntry> SiteScreeningEntries { get; }
    IQueryable<MethodologyVersion> MethodologyVersions { get; }
    IQueryable<MethodologyActivation> MethodologyActivations { get; }
    IQueryable<SiteVersionScore> SiteVersionScores { get; }

    Task<List<T>> ToListAsync<T>(IQueryable<T> query, CancellationToken ct = default);
    Task<int> CountAsync<T>(IQueryable<T> query, CancellationToken ct = default);
    Task<T?> FirstOrDefaultAsync<T>(IQueryable<T> query, CancellationToken ct = default);
}
