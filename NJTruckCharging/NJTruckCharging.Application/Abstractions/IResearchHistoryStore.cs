using NJTruckCharging.Domain.Entities;

namespace NJTruckCharging.Application.Abstractions;

/// <summary>
/// Append-only write surface for <see cref="ResearchHistoryEntry"/> (reads go through
/// <see cref="IReadStore.ResearchHistory"/>). There is deliberately no update or delete. Entries are staged on the
/// shared unit of work, so they are saved in the same transaction as the change they describe.
/// </summary>
public interface IResearchHistoryStore
{
    Task AppendAsync(ResearchHistoryEntry entry, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);

    /// <summary>Runs <paramref name="work"/> (which may save more than once) in one database transaction.</summary>
    Task<T> InTransactionAsync<T>(Func<Task<T>> work, CancellationToken ct = default);
}
