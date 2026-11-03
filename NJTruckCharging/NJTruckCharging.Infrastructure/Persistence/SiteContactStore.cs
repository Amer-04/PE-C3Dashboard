using Microsoft.EntityFrameworkCore;
using NJTruckCharging.Application.Abstractions;
using NJTruckCharging.Domain.Entities;

namespace NJTruckCharging.Infrastructure.Persistence;

public sealed class SiteContactStore(AppDbContext db) : ISiteContactStore
{
    public Task<SiteContact?> FindByIdAsync(int id, CancellationToken ct = default) =>
        db.SiteContacts.FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task AddAsync(SiteContact contact, CancellationToken ct = default) =>
        await db.SiteContacts.AddAsync(contact, ct);

    public void Remove(SiteContact contact) => db.SiteContacts.Remove(contact);

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
