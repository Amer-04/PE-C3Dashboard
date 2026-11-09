using System.Globalization;
using NJTruckCharging.Domain.Entities;
using NJTruckCharging.Domain.Methodology;

namespace NJTruckCharging.Application.Methodology;

/// <summary>Human-readable descriptions of rules (Methodology page, diffs) and of a site's inputs (score breakdown).</summary>
public static class CriterionText
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string CategoryLabel(CriterionCategory c) => c switch
    {
        CriterionCategory.Access => "Access",
        CriterionCategory.Site => "Site",
        CriterionCategory.TruckDemand => "Truck demand",
        CriterionCategory.ChargerGap => "Charger gap",
        CriterionCategory.Penalty => "Penalty",
        CriterionCategory.Bonus => "Bonus",
        _ => c.ToString()
    };

    public static string Pts(double v) => (v > 0 ? "+" : v < 0 ? "−" : "") + Math.Abs(v).ToString("0.##", Inv);

    /// <summary>"20 pts at ≤ 0.5 mi → 0 at ≥ 5 mi" / "≥ 3 ac: +20 · … · otherwise −15".</summary>
    public static string Rule(Criterion c, FieldCatalog? catalog = null)
    {
        catalog ??= FieldCatalog.Builtin;
        var field = catalog.Find(c.Field);
        var unit = field?.Unit is { } u ? " " + u : "";
        var text = c.Kind switch
        {
            CriterionKind.Linear when c.FullAt < c.ZeroAt =>
                $"{Pts(c.MaxPoints ?? 0)} at ≤ {N(c.FullAt)}{unit}, falling linearly to 0 at ≥ {N(c.ZeroAt)}{unit} ({field?.Label})",
            CriterionKind.Linear =>
                $"{Pts(c.MaxPoints ?? 0)} at ≥ {N(c.FullAt)}{unit}, falling linearly to 0 at ≤ {N(c.ZeroAt)}{unit} ({field?.Label})",
            CriterionKind.Percentile =>
                $"{Pts(c.MaxPoints ?? 0)} × percentile within the segment of {field?.Label}",
            _ => string.Join(" · ", c.Cases.Select(x => $"{x.Label ?? Conditions(x, catalog)}: {Pts(x.Points)}")
                     .Append($"otherwise {Pts(c.DefaultPoints)}"))
        };
        if (MethodologyEngine.UsesMissableField(c, catalog))
            text += $" · missing data: {(c.Missing.Mode == MissingDataMode.FixedPoints ? Pts(c.Missing.Points) : "0")}";
        return text;
    }

    public static string Conditions(ScoreCase sc, FieldCatalog? catalog = null) => string.Join(" and ", sc.Conditions.Select(c => Condition(c, catalog)));

    public static string Condition(Condition c, FieldCatalog? catalog = null)
    {
        var f = (catalog ?? FieldCatalog.Builtin).Find(c.Field);
        var label = f?.Label ?? c.Field;
        var unit = f?.Unit is { } u ? " " + u : "";
        return c.Op switch
        {
            ConditionOp.Lt => $"{label} < {N(c.Value)}{unit}",
            ConditionOp.Lte => $"{label} ≤ {N(c.Value)}{unit}",
            ConditionOp.Gt => $"{label} > {N(c.Value)}{unit}",
            ConditionOp.Gte => $"{label} ≥ {N(c.Value)}{unit}",
            ConditionOp.Eq => $"{label} = {N(c.Value)}{unit}",
            ConditionOp.Neq => $"{label} ≠ {N(c.Value)}{unit}",
            ConditionOp.Between => $"{label} {N(c.Value)}–{N(c.Value2)}{unit}",
            ConditionOp.IsTrue => label,
            ConditionOp.IsFalse => $"not {label}",
            ConditionOp.In => $"{label} is {string.Join("/", c.Values)}",
            ConditionOp.NotIn => $"{label} is not {string.Join("/", c.Values)}",
            ConditionOp.AnyLotIn => $"any lot is {string.Join("/", c.Values)}",
            _ => label
        };
    }

    /// <summary>The site's own input values for a criterion, e.g. "Distance to eligible exit: 1.23 mi".</summary>
    public static string SiteInputs(Criterion c, CandidateSite s, FieldCatalog? catalog = null)
    {
        catalog ??= FieldCatalog.Builtin;
        var parts = MethodologyEngine.FieldsUsed(c).Select(catalog.Find).Where(f => f is not null)
            .Select(f => $"{f!.Label}: {Value(f, f.Read(s))}");
        return string.Join(" · ", parts);
    }

    public static string Value(ScoringField f, FieldValue v)
    {
        if (v.IsMissing) return "no data";
        return f.Type switch
        {
            FieldType.Number => f.Unit switch
            {
                "$" => "$" + N(v.Number),
                null => N(v.Number),
                var u => $"{N(v.Number)} {u}"
            },
            FieldType.Boolean => v.Flag == true ? "yes" : "no",
            FieldType.Category => v.Text!,
            FieldType.LotCategory => string.Join("/", v.Lots!),
            _ => ""
        };
    }

    private static string N(double? v) => v is null ? "?" : Math.Abs(v.Value) >= 1000 ? v.Value.ToString("N0", Inv) : v.Value.ToString("0.###", Inv);
}
