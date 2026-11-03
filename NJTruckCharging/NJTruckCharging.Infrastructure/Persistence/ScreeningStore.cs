using Microsoft.EntityFrameworkCore;
using NJTruckCharging.Application.Abstractions;
using NJTruckCharging.Domain.Entities;

namespace NJTruckCharging.Infrastructure.Persistence;

public sealed class ScreeningStore(AppDbContext db) : IScreeningStore
{
    public Task<SiteScreening?> FindAsync(string siteId, CancellationToken ct = default) =>
        db.SiteScreenings.FirstOrDefaultAsync(x => x.SiteId == siteId, ct);

    public async Task AddAsync(SiteScreening screening, CancellationToken ct = default) =>
        await db.SiteScreenings.AddAsync(screening, ct);

    public async Task AddEntryAsync(SiteScreeningEntry entry, CancellationToken ct = default) =>
        await db.SiteScreeningEntries.AddAsync(entry, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
