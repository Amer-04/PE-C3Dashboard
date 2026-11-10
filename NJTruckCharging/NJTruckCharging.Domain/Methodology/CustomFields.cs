using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using NJTruckCharging.Domain.Entities;

namespace NJTruckCharging.Domain.Methodology;

/// <summary>A parsed, type-checked custom value (exactly one member set) or the reason it is invalid.</summary>
public readonly record struct ParsedValue(double? Number, bool? Flag, string? Category, string? Error)
{
    public bool Ok => Error is null;
}

/// <summary>
/// Rules for user-defined custom data fields: definition validation, strict typed value parsing (the same parser for
/// manual entry and dataset import) and exposure to the scoring engine as an ordinary typed <see cref="ScoringField"/>.
/// User text is only ever stored as a label/description/value — never evaluated.
/// </summary>
public static partial class CustomFields
{
    public const string KeyPrefix = "cf";
    public const int MaxLabelLength = 80;
    public const int MaxDescriptionLength = 600;
    public const int MaxCategories = 20;
    public const int MaxCategoryLength = 40;
    public const int MaxEvidenceLength = 2000;
    public const string MissingMeaning = "No value recorded or imported for this site yet (Unknown).";

    [GeneratedRegex("^cf[A-Z][A-Za-z0-9]{1,38}$")]
    private static partial Regex KeyPattern();

    public static bool IsValidKey(string? key) => key is not null && KeyPattern().IsMatch(key);

    /// <summary>"Utility capacity letter received" → "cfUtilityCapacityLetterReceived" (ASCII letters/digits only).</summary>
    public static string KeyFromLabel(string label)
    {
        var sb = new StringBuilder(KeyPrefix);
        foreach (var word in Regex.Split(label.Normalize(NormalizationForm.FormKD), "[^A-Za-z0-9]+").Where(w => w.Length > 0))
            sb.Append(char.ToUpperInvariant(word[0])).Append(word.AsSpan(1));
        var key = sb.Length > 40 ? sb.ToString(0, 40) : sb.ToString();
        if (key.Length < 4 || !char.IsUpper(key[2])) key = "cfField" + (key.Length > 2 ? key[2..] : "");
        return key.Length > 40 ? key[..40] : key;
    }

    /// <summary>Validation errors for a field definition (empty = valid).</summary>
    public static List<string> ValidateDefinition(CustomDataField f)
    {
        var e = new List<string>();
        if (!IsValidKey(f.Key)) e.Add("Key must look like 'cfSomething' (letters/digits).");
        if (ScoringFieldCatalog.Find(f.Key) is not null) e.Add($"'{f.Key}' is a built-in field.");
        if (string.IsNullOrWhiteSpace(f.Label)) e.Add("A name is required.");
        else if (f.Label.Length > MaxLabelLength) e.Add($"Name is limited to {MaxLabelLength} characters.");
        if ((f.Description ?? "").Length > MaxDescriptionLength) e.Add($"Description is limited to {MaxDescriptionLength} characters.");
        if (!Enum.IsDefined(f.Type)) e.Add("Unknown data type.");
        if (!Enum.IsDefined(f.Source)) e.Add("Unknown value source.");
        if ((f.Unit ?? "").Length > 20) e.Add("Unit is limited to 20 characters.");
        if (f.Type == CustomFieldType.Number)
        {
            if (f.MinValue is { } mn && !double.IsFinite(mn) || f.MaxValue is { } mx && !double.IsFinite(mx)) e.Add("Bounds must be finite numbers.");
            if (f.MinValue is { } lo && f.MaxValue is { } hi && lo > hi) e.Add("Minimum must be ≤ maximum.");
        }
        if (f.Type == CustomFieldType.Category)
        {
            var cats = f.CategoryList;
            if (cats.Count < 2) e.Add("A category field needs at least two allowed values.");
            if (cats.Count > MaxCategories) e.Add($"At most {MaxCategories} categories.");
            if (cats.Any(c => c.Length > MaxCategoryLength)) e.Add($"Categories are limited to {MaxCategoryLength} characters.");
            if (cats.Distinct(StringComparer.OrdinalIgnoreCase).Count() != cats.Count) e.Add("Categories must be unique.");
        }
        return e;
    }

    /// <summary>Strictly parses a raw text value for <paramref name="f"/>. Empty text is an error (omit the row to keep Unknown).</summary>
    public static ParsedValue Parse(CustomDataField f, string? raw)
    {
        var v = raw?.Trim() ?? "";
        if (v.Length == 0) return new(null, null, null, "Value is empty (leave the site out to keep it Unknown).");
        switch (f.Type)
        {
            case CustomFieldType.Number:
                if (!double.TryParse(v, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var d) || !double.IsFinite(d))
                    return new(null, null, null, $"'{Trunc(v)}' is not a number.");
                if (f.MinValue is { } lo && d < lo) return new(null, null, null, $"{d.ToString(CultureInfo.InvariantCulture)} is below the minimum {lo.ToString(CultureInfo.InvariantCulture)}.");
                if (f.MaxValue is { } hi && d > hi) return new(null, null, null, $"{d.ToString(CultureInfo.InvariantCulture)} is above the maximum {hi.ToString(CultureInfo.InvariantCulture)}.");
                return new(d, null, null, null);
            case CustomFieldType.Boolean:
                return v.ToLowerInvariant() switch
                {
                    "yes" or "y" or "true" or "1" => new(null, true, null, null),
                    "no" or "n" or "false" or "0" => new(null, false, null, null),
                    _ => new(null, null, null, $"'{Trunc(v)}' is not yes/no.")
                };
            default:
                var match = f.CategoryList.FirstOrDefault(c => string.Equals(c, v, StringComparison.OrdinalIgnoreCase));
                return match is null
                    ? new(null, null, null, $"'{Trunc(v)}' is not one of: {string.Join(", ", f.CategoryList)}.")
                    : new(null, null, match, null);
        }
    }

    public static string Display(CustomDataField f, CustomFieldValue v) => f.Type switch
    {
        CustomFieldType.Number => (v.NumberValue?.ToString("0.###", CultureInfo.InvariantCulture) ?? "") + (f.Unit is { Length: > 0 } u ? " " + u : ""),
        CustomFieldType.Boolean => v.BoolValue == true ? "yes" : v.BoolValue == false ? "no" : "",
        _ => v.CategoryValue ?? ""
    };

    public static FieldValue ToFieldValue(CustomDataField f, CustomFieldValue v) => f.Type switch
    {
        CustomFieldType.Number => FieldValue.Num(v.NumberValue),
        CustomFieldType.Boolean => v.BoolValue is { } b ? FieldValue.Bool(b) : default,
        _ => FieldValue.Cat(v.CategoryValue)
    };

    /// <summary>The field as the engine sees it: a typed read of the stored value by SiteId (absent → missing).</summary>
    public static ScoringField ToScoringField(CustomDataField f, IReadOnlyDictionary<string, FieldValue> valuesBySiteId) =>
        new(f.Key, f.Label,
            f.Type switch { CustomFieldType.Number => FieldType.Number, CustomFieldType.Boolean => FieldType.Boolean, _ => FieldType.Category },
            string.IsNullOrWhiteSpace(f.Unit) ? null : f.Unit, "Custom data",
            (f.Description is { Length: > 0 } d ? d + " " : "") + (f.Source == CustomFieldSource.Manual
                ? "(Custom field — recorded per site in Site Details with source evidence.)"
                : "(Custom field — imported from a dataset by Site ID.)"),
            MissingMeaning,
            f.Type == CustomFieldType.Category ? f.CategoryList : null,
            s => valuesBySiteId.TryGetValue(s.SiteId, out var v) ? v : default)
        { IsCustom = true, CustomSource = f.Source };

    private static string Trunc(string v) => v.Length > 40 ? v[..40] + "…" : v;
}
