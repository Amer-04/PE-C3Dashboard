using Microsoft.EntityFrameworkCore;
using NJTruckCharging.Application.Abstractions;
using NJTruckCharging.Domain.Entities;

namespace NJTruckCharging.Infrastructure.Persistence;

public sealed class ResearchHistoryStore(AppDbContext db) : IResearchHistoryStore
{
    public async Task AppendAsync(ResearchHistoryEntry entry, CancellationToken ct = default) =>
        await db.ResearchHistory.AddAsync(entry, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);

    public Task<T> InTransactionAsync<T>(Func<Task<T>> work, CancellationToken ct = default) =>
        db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var result = await work();
            await tx.CommitAsync(ct);
            return result;
        });
}
