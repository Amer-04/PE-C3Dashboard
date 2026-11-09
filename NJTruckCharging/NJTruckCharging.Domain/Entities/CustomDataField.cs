namespace NJTruckCharging.Domain.Entities;

public enum CustomFieldType { Number = 0, Boolean = 1, Category = 2 }

public enum CustomFieldSource
{
    /// <summary>Reviewers record a value per site in Site Details, with source evidence.</summary>
    Manual = 0,
    /// <summary>Values come from a dataset imported by stable Site ID (validated, previewed, then committed).</summary>
    Imported = 1
}

/// <summary>
/// A user-defined, TYPED data attribute that methodology criteria may score (e.g. "Utility capacity letter received").
/// The name is free text; the engine only ever reads the typed value stored in <see cref="CustomFieldValue"/>.
/// Sites without a value are Unknown and get the criterion's explicit missing-data policy.
/// </summary>
public class CustomDataField
{
    public int Id { get; set; }
    /// <summary>Stable catalog key ("cf" + PascalCase), referenced by methodology rules. Never changes.</summary>
    public string Key { get; set; } = default!;
    public string Label { get; set; } = default!;
    public string? Description { get; set; }
    public CustomFieldType Type { get; set; }
    public CustomFieldSource Source { get; set; }
    /// <summary>Unit shown for numbers (e.g. "kW", "ft").</summary>
    public string? Unit { get; set; }
    /// <summary>Optional inclusive bounds; values outside are rejected as invalid.</summary>
    public double? MinValue { get; set; }
    public double? MaxValue { get; set; }
    /// <summary>Allowed values for Category fields, '|' separated (validated, case-insensitive).</summary>
    public string? Categories { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }

    public IReadOnlyList<string> CategoryList =>
        (Categories ?? "").Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}

/// <summary>
/// The value of one custom field for one site. Soft-referenced by the stable SiteId (no FK) so it survives workbook
/// re-imports. Exactly one typed column is set, matching the field's type. No row = Unknown.
/// </summary>
public class CustomFieldValue
{
    public int Id { get; set; }
    public string FieldKey { get; set; } = default!;
    public string SiteId { get; set; } = default!;
    public double? NumberValue { get; set; }
    public bool? BoolValue { get; set; }
    public string? CategoryValue { get; set; }
    /// <summary>Where the value came from (document, call, site visit, dataset) — required for manual entries.</summary>
    public string? Evidence { get; set; }
    public CustomFieldSource Source { get; set; }
    /// <summary>File name of the import batch that set this value (imported fields).</summary>
    public string? ImportBatch { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
