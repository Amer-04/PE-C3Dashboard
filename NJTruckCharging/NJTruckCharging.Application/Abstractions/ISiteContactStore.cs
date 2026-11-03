using NJTruckCharging.Domain.Entities;

namespace NJTruckCharging.Application.Abstractions;

/// <summary>Write surface for researched site contacts (reads/listing go through <see cref="IReadStore.SiteContacts"/>).</summary>
public interface ISiteContactStore
{
    Task<SiteContact?> FindByIdAsync(int id, CancellationToken ct = default);
    Task AddAsync(SiteContact contact, CancellationToken ct = default);
    void Remove(SiteContact contact);
    Task SaveChangesAsync(CancellationToken ct = default);
}
