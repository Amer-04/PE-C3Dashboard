using Microsoft.EntityFrameworkCore;
using NJTruckCharging.Application.Abstractions;
using NJTruckCharging.Domain.Entities;

namespace NJTruckCharging.Infrastructure.Persistence;

public sealed class SavedSiteStore(AppDbContext db) : ISavedSiteStore
{
    public Task<SavedSite?> FindByIdAsync(int id, CancellationToken ct = default) =>
        db.SavedSites.FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<SavedSite?> FindBySiteIdAsync(string siteId, CancellationToken ct = default) =>
        db.SavedSites.FirstOrDefaultAsync(x => x.SiteId == siteId, ct);

    public async Task AddAsync(SavedSite site, CancellationToken ct = default) =>
        await db.SavedSites.AddAsync(site, ct);

    public void Remove(SavedSite site) => db.SavedSites.Remove(site);

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
