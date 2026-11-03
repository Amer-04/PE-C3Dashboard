using Microsoft.EntityFrameworkCore;
using NJTruckCharging.Application.Abstractions;
using NJTruckCharging.Domain.Entities;

namespace NJTruckCharging.Infrastructure.Persistence;

public sealed class FeasibilityStore(AppDbContext db) : IFeasibilityStore
{
    public Task<FeasibilityItem?> FindAsync(string siteId, FeasibilityItemKey key, CancellationToken ct = default) =>
        db.FeasibilityItems.FirstOrDefaultAsync(x => x.SiteId == siteId && x.ItemKey == key, ct);

    public async Task AddAsync(FeasibilityItem item, CancellationToken ct = default) =>
        await db.FeasibilityItems.AddAsync(item, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
