using Microsoft.EntityFrameworkCore;
using NJTruckCharging.Application.Abstractions;
using NJTruckCharging.Domain.Entities;

namespace NJTruckCharging.Infrastructure.Persistence;

public sealed class CustomFieldStore(AppDbContext db) : ICustomFieldStore
{
    public Task<List<CustomDataField>> ListFieldsAsync(CancellationToken ct = default) =>
        db.CustomDataFields.AsNoTracking().OrderBy(f => f.Id).ToListAsync(ct);

    public Task<CustomDataField?> FindFieldAsync(string key, CancellationToken ct = default) =>
        db.CustomDataFields.FirstOrDefaultAsync(f => f.Key == key, ct);

    public async Task AddFieldAsync(CustomDataField field, CancellationToken ct = default)
    {
        db.CustomDataFields.Add(field);
        await db.SaveChangesAsync(ct);
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);

    public Task<List<CustomFieldValue>> AllValuesAsync(CancellationToken ct = default) =>
        db.CustomFieldValues.AsNoTracking().ToListAsync(ct);

    public Task<List<CustomFieldValue>> ValuesForFieldAsync(string key, CancellationToken ct = default) =>
        db.CustomFieldValues.AsNoTracking().Where(v => v.FieldKey == key).ToListAsync(ct);

    public Task<List<CustomFieldValue>> ValuesForSiteAsync(string siteId, CancellationToken ct = default) =>
        db.CustomFieldValues.AsNoTracking().Where(v => v.SiteId == siteId).ToListAsync(ct);

    public Task<CustomFieldValue?> FindValueAsync(string key, string siteId, CancellationToken ct = default) =>
        db.CustomFieldValues.FirstOrDefaultAsync(v => v.FieldKey == key && v.SiteId == siteId, ct);

    public async Task AddValueAsync(CustomFieldValue value, CancellationToken ct = default)
    {
        db.CustomFieldValues.Add(value);
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveValueAsync(CustomFieldValue value, CancellationToken ct = default)
    {
        db.CustomFieldValues.Remove(value);
        await db.SaveChangesAsync(ct);
    }

    public async Task<Dictionary<string, int>> CountsByFieldAsync(CancellationToken ct = default) =>
        await db.CustomFieldValues.AsNoTracking().GroupBy(v => v.FieldKey)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);

    public async Task<(int Inserted, int Updated)> UpsertImportAsync(string key, IReadOnlyList<CustomImportRow> rows, string batch,
        DateTimeOffset now, CancellationToken ct = default)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var existing = await db.CustomFieldValues.Where(v => v.FieldKey == key).ToDictionaryAsync(v => v.SiteId, StringComparer.OrdinalIgnoreCase, ct);
            int inserted = 0, updated = 0;
            foreach (var r in rows)
            {
                if (!existing.TryGetValue(r.SiteId, out var v))
                {
                    v = new CustomFieldValue { FieldKey = key, SiteId = r.SiteId, CreatedAtUtc = now };
                    db.CustomFieldValues.Add(v);
                    existing[r.SiteId] = v;
                    inserted++;
                }
                else updated++;
                v.NumberValue = r.Number; v.BoolValue = r.Flag; v.CategoryValue = r.Category;
                v.Evidence = r.Evidence; v.Source = CustomFieldSource.Imported; v.ImportBatch = batch; v.UpdatedAtUtc = now;
            }
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            db.ChangeTracker.Clear();
            return (inserted, updated);
        });
    }

    public async Task<HashSet<string>> SiteIdsAsync(CancellationToken ct = default) =>
        (await db.CandidateSites.AsNoTracking().Select(s => s.SiteId).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
}
