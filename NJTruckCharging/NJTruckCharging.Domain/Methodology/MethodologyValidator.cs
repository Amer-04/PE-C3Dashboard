using System.Text.RegularExpressions;

namespace NJTruckCharging.Domain.Methodology;

public enum IssueSeverity { Error, Warning }

public sealed record ValidationIssue(IssueSeverity Severity, string Path, string Message);

/// <summary>
/// Schema validation for <see cref="MethodologyRules"/>. Only catalog fields, type-appropriate operators and finite,
/// bounded numbers are accepted — rules are data, never expressions or code.
/// </summary>
public static partial class MethodologyValidator
{
    public const int MaxCriteria = 40;
    public const int MaxCases = 12;
    public const int MaxConditions = 4;
    public const double MaxAbsPoints = 100;
    public const int MaxNameLength = 80;
    public const int MaxExplanationLength = 600;

    [GeneratedRegex("^[a-z][a-zA-Z0-9]{1,39}$")]
    private static partial Regex KeyPattern();

    public static IReadOnlyList<ValidationIssue> Validate(MethodologyRules? rules, FieldCatalog? catalog = null)
    {
        catalog ??= FieldCatalog.Builtin;
        var issues = new List<ValidationIssue>();
        void Err(string path, string msg) => issues.Add(new(IssueSeverity.Error, path, msg));
        void Warn(string path, string msg) => issues.Add(new(IssueSeverity.Warning, path, msg));

        if (rules is null) { Err("", "Rules are required."); return issues; }
        if (rules.SchemaVersion != MethodologyRules.CurrentSchemaVersion)
            Err("schemaVersion", $"Unsupported schema version {rules.SchemaVersion}.");
        if (rules.Criteria is null || rules.Criteria.Count == 0) { Err("criteria", "At least one criterion is required."); return issues; }
        if (rules.Criteria.Count > MaxCriteria) Err("criteria", $"At most {MaxCriteria} criteria are allowed.");
        if (!rules.Criteria.Any(c => c.Enabled)) Err("criteria", "At least one criterion must be enabled.");

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < rules.Criteria.Count; i++)
        {
            var c = rules.Criteria[i];
            var p = $"criteria[{i}]";
            if (c is null) { Err(p, "Criterion is empty."); continue; }

            if (string.IsNullOrWhiteSpace(c.Key) || !KeyPattern().IsMatch(c.Key))
                Err($"{p}.key", "Key must start with a lower-case letter and contain only letters/digits (2–40 chars).");
            else if (!seen.Add(c.Key)) Err($"{p}.key", $"Duplicate key '{c.Key}'.");

            if (string.IsNullOrWhiteSpace(c.Name)) Err($"{p}.name", "Name is required.");
            else if (c.Name.Length > MaxNameLength) Err($"{p}.name", $"Name is limited to {MaxNameLength} characters.");
            if ((c.Explanation ?? "").Length > MaxExplanationLength) Err($"{p}.explanation", $"Explanation is limited to {MaxExplanationLength} characters.");
            if (!Enum.IsDefined(c.Category)) Err($"{p}.category", "Unknown category.");
            if (!Enum.IsDefined(c.Kind)) { Err($"{p}.kind", "Unknown criterion type."); continue; }

            CheckPoints($"{p}.missing.points", c.Missing?.Points ?? 0, Err);
            if (c.Missing is null || !Enum.IsDefined(c.Missing.Mode)) Err($"{p}.missing", "A missing-data policy is required.");

            switch (c.Kind)
            {
                case CriterionKind.Linear:
                {
                    var f = NumberField($"{p}.field", c.Field, Err, catalog);
                    if (c.FullAt is not { } full || !double.IsFinite(full)) Err($"{p}.fullAt", "Full-points threshold is required.");
                    if (c.ZeroAt is not { } zero || !double.IsFinite(zero)) Err($"{p}.zeroAt", "Zero-points threshold is required.");
                    if (c.FullAt is { } a && c.ZeroAt is { } b && a == b) Err($"{p}.zeroAt", "Full-points and zero-points thresholds must differ.");
                    if (c.MaxPoints is not { } mx) Err($"{p}.maxPoints", "Max points is required.");
                    else CheckPoints($"{p}.maxPoints", mx, Err);
                    if (c.Cases is { Count: > 0 }) Err($"{p}.cases", "Linear criteria do not use cases.");
                    _ = f;
                    break;
                }
                case CriterionKind.Percentile:
                    NumberField($"{p}.field", c.Field, Err, catalog);
                    if (c.MaxPoints is not { } pmx) Err($"{p}.maxPoints", "Max points is required.");
                    else CheckPoints($"{p}.maxPoints", pmx, Err);
                    if (c.Cases is { Count: > 0 }) Err($"{p}.cases", "Percentile criteria do not use cases.");
                    break;
                case CriterionKind.Cases:
                    CheckPoints($"{p}.defaultPoints", c.DefaultPoints, Err);
                    if (c.Cases is null || c.Cases.Count == 0) { Err($"{p}.cases", "At least one case is required."); break; }
                    if (c.Cases.Count > MaxCases) Err($"{p}.cases", $"At most {MaxCases} cases are allowed.");
                    for (var k = 0; k < c.Cases.Count; k++) ValidateCase($"{p}.cases[{k}]", c.Cases[k], Err, catalog);
                    WarnUnreachable(p, c, Warn);
                    break;
            }

            if (c.Enabled && MethodologyEngine.UsesMissableField(c, catalog) && issues.All(x => x.Severity != IssueSeverity.Error || !x.Path.StartsWith(p)))
            {
                var fields = MethodologyEngine.FieldsUsed(c).Select(catalog.Find).Where(f => f?.CanBeMissing == true).Select(f => f!.Label);
                Warn($"{p}.missing", $"Uses data that is missing for some sites ({string.Join(", ", fields)}): those sites get " +
                    (c.Missing!.Mode == MissingDataMode.FixedPoints ? $"{c.Missing.Points:0.##} points" : "0 points") + " (no values are invented).");
            }
        }
        return issues;
    }

    private static ScoringField? NumberField(string path, string? key, Action<string, string> err, FieldCatalog catalog)
    {
        var f = catalog.Find(key);
        if (f is null) { err(path, $"Unknown or unsupported data field '{key}'."); return null; }
        if (f.Type != FieldType.Number) { err(path, $"'{f.Label}' is not numeric."); return null; }
        return f;
    }

    private static void ValidateCase(string p, ScoreCase sc, Action<string, string> err, FieldCatalog catalog)
    {
        if (sc is null) { err(p, "Case is empty."); return; }
        CheckPoints($"{p}.points", sc.Points, err);
        if ((sc.Label ?? "").Length > MaxNameLength) err($"{p}.label", $"Label is limited to {MaxNameLength} characters.");
        if (sc.Conditions is null || sc.Conditions.Count == 0) { err($"{p}.conditions", "At least one condition is required."); return; }
        if (sc.Conditions.Count > MaxConditions) err($"{p}.conditions", $"At most {MaxConditions} conditions per case.");
        for (var j = 0; j < sc.Conditions.Count; j++)
        {
            var cp = $"{p}.conditions[{j}]";
            var cond = sc.Conditions[j];
            if (cond is null) { err(cp, "Condition is empty."); continue; }
            var f = catalog.Find(cond.Field);
            if (f is null) { err($"{cp}.field", $"Unknown or unsupported data field '{cond.Field}'."); continue; }
            if (!Enum.IsDefined(cond.Op) || !ScoringFieldCatalog.OperatorsFor(f.Type).Contains(cond.Op))
            {
                err($"{cp}.op", $"Operator '{cond.Op}' cannot be used with '{f.Label}' ({f.Type}).");
                continue;
            }
            switch (f.Type)
            {
                case FieldType.Number:
                    if (cond.Value is not { } v || !double.IsFinite(v)) err($"{cp}.value", "Enter a number for this condition.");
                    if (cond.Op == ConditionOp.Between)
                    {
                        if (cond.Value2 is not { } v2 || !double.IsFinite(v2)) err($"{cp}.value2", "An upper bound is required for 'between'.");
                        else if (cond.Value is { } lo && lo > v2) err($"{cp}.value2", "Upper bound must be ≥ lower bound.");
                    }
                    break;
                case FieldType.Category:
                case FieldType.LotCategory:
                    if (cond.Values is null || cond.Values.Count == 0) err($"{cp}.values", "Choose at least one value.");
                    else
                    {
                        var bad = cond.Values.Where(v => !f.Categories!.Contains(v, StringComparer.OrdinalIgnoreCase)).ToList();
                        if (bad.Count > 0) err($"{cp}.values", $"Unknown value(s) {string.Join(", ", bad)}; allowed: {string.Join(", ", f.Categories!)}.");
                    }
                    break;
            }
        }
    }

    /// <summary>Warns when a single-condition threshold band can never match because an earlier band always catches it.</summary>
    private static void WarnUnreachable(string p, Criterion c, Action<string, string> warn)
    {
        for (var j = 1; j < c.Cases.Count; j++)
        {
            if (c.Cases[j]?.Conditions is not [var later]) continue;
            for (var i = 0; i < j; i++)
            {
                if (c.Cases[i]?.Conditions is not [var earlier] || earlier.Field != later.Field) continue;
                if (earlier.Value is not { } a || later.Value is not { } b) continue;
                var shadowed = (earlier.Op, later.Op) switch
                {
                    (ConditionOp.Gte or ConditionOp.Gt, ConditionOp.Gte or ConditionOp.Gt) => b > a || (b == a && (earlier.Op == ConditionOp.Gte || later.Op == ConditionOp.Gt)),
                    (ConditionOp.Lte or ConditionOp.Lt, ConditionOp.Lte or ConditionOp.Lt) => b < a || (b == a && (earlier.Op == ConditionOp.Lte || later.Op == ConditionOp.Lt)),
                    _ => false
                };
                if (shadowed) { warn($"{p}.cases[{j}]", $"Case {j + 1} can never match: case {i + 1} catches every site first. Order bands from most to least restrictive."); break; }
            }
        }
    }

    private static void CheckPoints(string path, double v, Action<string, string> err)
    {
        if (!double.IsFinite(v)) err(path, "Enter a number for the points.");
        else if (Math.Abs(v) > MaxAbsPoints) err(path, $"Points must be between −{MaxAbsPoints} and {MaxAbsPoints}.");
    }
}
