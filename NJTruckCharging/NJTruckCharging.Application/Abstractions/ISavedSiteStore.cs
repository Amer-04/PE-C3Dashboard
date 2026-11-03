using NJTruckCharging.Domain.Entities;

namespace NJTruckCharging.Application.Abstractions;

/// <summary>
/// Write surface for user-saved sites. Reads for listing go through <see cref="IReadStore.SavedSites"/>;
/// these methods return tracked entities for mutation.
/// </summary>
public interface ISavedSiteStore
{
    Task<SavedSite?> FindByIdAsync(int id, CancellationToken ct = default);
    Task<SavedSite?> FindBySiteIdAsync(string siteId, CancellationToken ct = default);
    Task AddAsync(SavedSite site, CancellationToken ct = default);
    void Remove(SavedSite site);
    Task SaveChangesAsync(CancellationToken ct = default);
}
