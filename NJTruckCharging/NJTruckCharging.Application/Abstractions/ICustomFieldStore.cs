using NJTruckCharging.Domain.Entities;

namespace NJTruckCharging.Application.Abstractions;

/// <summary>One validated row of a custom-field import, ready to upsert.</summary>
public sealed record CustomImportRow(string SiteId, double? Number, bool? Flag, string? Category, string? Evidence);

/// <summary>Persistence of user-defined custom data fields and their per-site values (soft-referenced by SiteId).</summary>
public interface ICustomFieldStore
{
    Task<List<CustomDataField>> ListFieldsAsync(CancellationToken ct = default);
    /// <summary>Tracked.</summary>
    Task<CustomDataField?> FindFieldAsync(string key, CancellationToken ct = default);
    Task AddFieldAsync(CustomDataField field, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);

    Task<List<CustomFieldValue>> AllValuesAsync(CancellationToken ct = default);
    Task<List<CustomFieldValue>> ValuesForFieldAsync(string key, CancellationToken ct = default);
    Task<List<CustomFieldValue>> ValuesForSiteAsync(string siteId, CancellationToken ct = default);
    /// <summary>Tracked.</summary>
    Task<CustomFieldValue?> FindValueAsync(string key, string siteId, CancellationToken ct = default);
    Task AddValueAsync(CustomFieldValue value, CancellationToken ct = default);
    Task RemoveValueAsync(CustomFieldValue value, CancellationToken ct = default);
    Task<Dictionary<string, int>> CountsByFieldAsync(CancellationToken ct = default);

    /// <summary>Upserts validated import rows for one field in a single transaction. Returns (inserted, updated).</summary>
    Task<(int Inserted, int Updated)> UpsertImportAsync(string key, IReadOnlyList<CustomImportRow> rows, string batch,
        DateTimeOffset now, CancellationToken ct = default);

    /// <summary>All stable SiteIds currently in the candidate dataset.</summary>
    Task<HashSet<string>> SiteIdsAsync(CancellationToken ct = default);
}
