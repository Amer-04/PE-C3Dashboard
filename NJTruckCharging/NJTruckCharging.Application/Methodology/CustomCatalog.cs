using NJTruckCharging.Application.Abstractions;
using NJTruckCharging.Domain.Entities;
using NJTruckCharging.Domain.Methodology;

namespace NJTruckCharging.Application.Methodology;

/// <summary>Builds the field catalog (built-in + custom fields with their current typed values) for scoring.</summary>
public static class CustomCatalog
{
    /// <param name="siteId">Only load values for this site (Site Details); null = every site (scoring).</param>
    public static async Task<FieldCatalog> BuildAsync(ICustomFieldStore store, string? siteId = null, CancellationToken ct = default)
    {
        var fields = await store.ListFieldsAsync(ct);
        if (fields.Count == 0) return FieldCatalog.Builtin;
        var values = siteId is null ? await store.AllValuesAsync(ct) : await store.ValuesForSiteAsync(siteId, ct);
        return Build(fields, values);
    }

    public static FieldCatalog Build(IReadOnlyList<CustomDataField> fields, IReadOnlyList<CustomFieldValue> values)
    {
        var byKey = values.GroupBy(v => v.FieldKey).ToDictionary(g => g.Key, g => g.ToList());
        return FieldCatalog.WithCustom(fields.Select(f =>
        {
            var dict = (byKey.GetValueOrDefault(f.Key) ?? [])
                .ToDictionary(v => v.SiteId, v => CustomFields.ToFieldValue(f, v), StringComparer.Ordinal);
            return CustomFields.ToScoringField(f, dict);
        }));
    }
}
