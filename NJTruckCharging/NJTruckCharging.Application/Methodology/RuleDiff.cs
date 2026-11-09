using NJTruckCharging.Domain.Methodology;

namespace NJTruckCharging.Application.Methodology;

public enum RuleChangeKind { Added, Removed, Modified, Enabled, Disabled }

public sealed record RuleChangeDto(string Key, string Name, RuleChangeKind Change, IReadOnlyList<string> Details);

/// <summary>Criterion-level differences between two rule sets (matched by key).</summary>
public static class RuleDiff
{
    public static List<RuleChangeDto> Compare(MethodologyRules from, MethodologyRules to, FieldCatalog? catalog = null)
    {
        var changes = new List<RuleChangeDto>();
        var old = from.Criteria.ToDictionary(c => c.Key);
        var neu = to.Criteria.ToDictionary(c => c.Key);

        foreach (var c in to.Criteria)
        {
            if (!old.TryGetValue(c.Key, out var o))
            {
                changes.Add(new(c.Key, c.Name, RuleChangeKind.Added, [CriterionText.Rule(c, catalog) + (c.Enabled ? "" : " (disabled)")]));
                continue;
            }
            var details = new List<string>();
            if (o.Name != c.Name) details.Add($"Name: “{o.Name}” → “{c.Name}”");
            if (o.Explanation != c.Explanation) details.Add("Explanation edited");
            if (o.Category != c.Category) details.Add($"Category: {CriterionText.CategoryLabel(o.Category)} → {CriterionText.CategoryLabel(c.Category)}");
            var oRule = CriterionText.Rule(o, catalog);
            var nRule = CriterionText.Rule(c, catalog);
            if (oRule != nRule || o.Kind != c.Kind || o.Field != c.Field) details.Add($"Rule: {oRule}  →  {nRule}");
            else if (MethodologyJson.Serialize(new MethodologyRules { Criteria = [o with { Enabled = true, Name = "", Explanation = "" }] })
                     != MethodologyJson.Serialize(new MethodologyRules { Criteria = [c with { Enabled = true, Name = "", Explanation = "" }] }))
                details.Add("Case labels or conditions edited");

            if (o.Enabled != c.Enabled)
                changes.Add(new(c.Key, c.Name, c.Enabled ? RuleChangeKind.Enabled : RuleChangeKind.Disabled, details));
            else if (details.Count > 0)
                changes.Add(new(c.Key, c.Name, RuleChangeKind.Modified, details));
        }
        foreach (var o in from.Criteria.Where(o => !neu.ContainsKey(o.Key)))
            changes.Add(new(o.Key, o.Name, RuleChangeKind.Removed, [CriterionText.Rule(o, catalog)]));

        var oldOrder = from.Criteria.Select(c => c.Key).Where(neu.ContainsKey).ToList();
        var newOrder = to.Criteria.Select(c => c.Key).Where(old.ContainsKey).ToList();
        if (!oldOrder.SequenceEqual(newOrder))
            changes.Add(new("(order)", "Criterion order", RuleChangeKind.Modified, ["Criteria were reordered (affects case evaluation only within a criterion; totals unchanged)"]));
        return changes;
    }
}
