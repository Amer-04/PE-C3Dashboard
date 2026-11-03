using NJTruckCharging.Domain.Entities;

namespace NJTruckCharging.Application.Abstractions;

/// <summary>Write surface for screening decisions (reads go through <see cref="IReadStore.SiteScreenings"/>).</summary>
public interface IScreeningStore
{
    Task<SiteScreening?> FindAsync(string siteId, CancellationToken ct = default);
    Task AddAsync(SiteScreening screening, CancellationToken ct = default);
    Task AddEntryAsync(SiteScreeningEntry entry, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
