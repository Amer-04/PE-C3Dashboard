using Microsoft.EntityFrameworkCore;
using NJTruckCharging.Application.Abstractions;
using NJTruckCharging.Domain.Entities;

namespace NJTruckCharging.Infrastructure.Persistence;

public sealed class ReadStore(AppDbContext db) : IReadStore
{
    public IQueryable<CandidateSite> Sites => db.CandidateSites.AsNoTracking();
    public IQueryable<NearbyFleet> NearbyFleets => db.NearbyFleets.AsNoTracking();
    public IQueryable<SegmentInfo> Segments => db.Segments.AsNoTracking();
    public IQueryable<PowerEdisonSite> PowerEdisonSites => db.PowerEdisonSites.AsNoTracking();
    public IQueryable<Interchange> Interchanges => db.Interchanges.AsNoTracking();
    public IQueryable<MethodologyNote> MethodologyNotes => db.MethodologyNotes.AsNoTracking();
    public IQueryable<SavedSite> SavedSites => db.SavedSites.AsNoTracking();
    public IQueryable<SiteContact> SiteContacts => db.SiteContacts.AsNoTracking();
    public IQueryable<FeasibilityItem> FeasibilityItems => db.FeasibilityItems.AsNoTracking();
    public IQueryable<SiteScreening> SiteScreenings => db.SiteScreenings.AsNoTracking();
    public IQueryable<SiteScreeningEntry> SiteScreeningEntries => db.SiteScreeningEntries.AsNoTracking();

    public Task<List<T>> ToListAsync<T>(IQueryable<T> query, CancellationToken ct = default) =>
        EntityFrameworkQueryableExtensions.ToListAsync(query, ct);

    public Task<int> CountAsync<T>(IQueryable<T> query, CancellationToken ct = default) =>
        EntityFrameworkQueryableExtensions.CountAsync(query, ct);

    public Task<T?> FirstOrDefaultAsync<T>(IQueryable<T> query, CancellationToken ct = default) =>
        EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(query, ct);
}
