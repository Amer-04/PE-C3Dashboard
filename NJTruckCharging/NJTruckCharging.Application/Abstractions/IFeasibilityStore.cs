using NJTruckCharging.Domain.Entities;

namespace NJTruckCharging.Application.Abstractions;

/// <summary>Write surface for feasibility checklist items (reads go through <see cref="IReadStore.FeasibilityItems"/>).</summary>
public interface IFeasibilityStore
{
    Task<FeasibilityItem?> FindAsync(string siteId, FeasibilityItemKey key, CancellationToken ct = default);
    Task AddAsync(FeasibilityItem item, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
