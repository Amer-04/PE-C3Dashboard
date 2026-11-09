using NJTruckCharging.Domain.Entities;

namespace NJTruckCharging.Domain.Methodology;

/// <summary>Score and rank of one site under one methodology.</summary>
public sealed class SiteScoreResult
{
    public required string SiteId { get; init; }
    public required int Segment { get; init; }
    /// <summary>Rounded to 0.1 exactly like the original engine.</summary>
    public double Score { get; set; }
    public int Rank { get; set; }
    /// <summary>Unrounded points per criterion, aligned with <see cref="MethodologyResult.Keys"/> (disabled criteria = 0).</summary>
    public required double[] Points { get; init; }
    /// <summary>True where the site lacked data for that criterion and its missing-data policy was applied.</summary>
    public required bool[] Missing { get; init; }
}

public sealed class MethodologyResult
{
    public required IReadOnlyList<string> Keys { get; init; }
    public required IReadOnlyList<SiteScoreResult> Sites { get; init; }

    public double PointsFor(SiteScoreResult s, string key)
    {
        for (var i = 0; i < Keys.Count; i++) if (Keys[i] == key) return s.Points[i];
        return 0;
    }
}

/// <summary>
/// Evaluates a validated <see cref="MethodologyRules"/> over all candidate sites. Pure and deterministic:
/// <list type="bullet">
/// <item>Percentile criteria rank within the NOFA segment, ties sharing the average rank (pandas <c>rank(pct=True)</c>);
/// sites missing the field are excluded from the ranking and receive the missing-data policy.</item>
/// <item>Score = sum of enabled criteria in list order, rounded to 0.1 (same as the original engine).</item>
/// <item>Rank within segment: score ↓, distance to exit ↑, original workbook rank ↑, Site ID ↑ — a total order,
/// so equal scores always rank the same way regardless of load order.</item>
/// </list>
/// </summary>
public static class MethodologyEngine
{
    public static MethodologyResult Evaluate(IReadOnlyList<CandidateSite> sites, MethodologyRules rules, FieldCatalog? catalog = null)
    {
        catalog ??= FieldCatalog.Builtin;
        var criteria = rules.Criteria;
        var keys = criteria.Select(c => c.Key).ToArray();
        var n = sites.Count;
        var results = new SiteScoreResult[n];
        for (var i = 0; i < n; i++)
            results[i] = new SiteScoreResult
            {
                SiteId = sites[i].SiteId, Segment = sites[i].Segment,
                Points = new double[criteria.Count], Missing = new bool[criteria.Count]
            };

        for (var c = 0; c < criteria.Count; c++)
        {
            var crit = criteria[c];
            if (!crit.Enabled) continue;
            if (crit.Kind == CriterionKind.Percentile)
            {
                ScorePercentile(sites, results, c, crit, catalog);
                continue;
            }
            for (var i = 0; i < n; i++)
            {
                var (points, missing) = crit.Kind == CriterionKind.Linear ? ScoreLinear(sites[i], crit, catalog) : ScoreCases(sites[i], crit, catalog);
                results[i].Points[c] = points;
                results[i].Missing[c] = missing;
            }
        }

        for (var i = 0; i < n; i++)
        {
            double total = 0;
            var p = results[i].Points;
            for (var c = 0; c < p.Length; c++) total += p[c];
            results[i].Score = Math.Round(total, 1);
        }

        AssignRanks(sites, results);
        return new MethodologyResult { Keys = keys, Sites = results };
    }

    /// <summary>Score ↓, miles to exit ↑, workbook rank ↑, SiteId ↑ (ordinal) — within each segment.</summary>
    public static void AssignRanks(IReadOnlyList<CandidateSite> sites, IReadOnlyList<SiteScoreResult> results)
    {
        foreach (var seg in Enumerable.Range(0, sites.Count).GroupBy(i => sites[i].Segment))
        {
            var rank = 1;
            foreach (var i in seg
                         .OrderByDescending(i => results[i].Score)
                         .ThenBy(i => sites[i].MilesToExit)
                         .ThenBy(i => sites[i].RankInSegment)
                         .ThenBy(i => sites[i].SiteId, StringComparer.Ordinal))
                results[i].Rank = rank++;
        }
    }

    public static (double Points, bool Missing) ScoreLinear(CandidateSite s, Criterion c, FieldCatalog? catalog = null)
    {
        var field = (catalog ?? FieldCatalog.Builtin).Find(c.Field)!;
        var v = field.Read(s).Number;
        if (v is null) return (c.Missing.Award, true);
        double max = c.MaxPoints ?? 0, full = c.FullAt ?? 0, zero = c.ZeroAt ?? 0;
        var lowerIsBetter = full < zero;
        if (lowerIsBetter ? v <= full : v >= full) return (max, false);
        // Same arithmetic as the original engine: max × (zero − v) / (zero − full), clamped to [0, max].
        var raw = max * (v.Value - zero) / (full - zero);
        return (Math.Clamp(raw, Math.Min(0, max), Math.Max(0, max)), false);
    }

    public static (double Points, bool Missing) ScoreCases(CandidateSite s, Criterion c, FieldCatalog? catalog = null)
    {
        catalog ??= FieldCatalog.Builtin;
        // A site lacking ANY field this criterion uses gets the missing-data policy (never a guessed case).
        foreach (var sc in c.Cases)
            foreach (var cond in sc.Conditions)
                if (catalog.Find(cond.Field)!.Read(s).IsMissing) return (c.Missing.Award, true);

        foreach (var sc in c.Cases)
        {
            var all = true;
            foreach (var cond in sc.Conditions)
                if (Test(s, cond, catalog) != true) { all = false; break; }
            if (all) return (sc.Points, false);
        }
        return (c.DefaultPoints, false);
    }

    /// <summary>True/false, or null when the site has no value for the field.</summary>
    public static bool? Test(CandidateSite s, Condition cond, FieldCatalog? catalog = null)
    {
        var field = (catalog ?? FieldCatalog.Builtin).Find(cond.Field)!;
        var v = field.Read(s);
        if (v.IsMissing) return null;
        return cond.Op switch
        {
            ConditionOp.Lt => v.Number < cond.Value,
            ConditionOp.Lte => v.Number <= cond.Value,
            ConditionOp.Gt => v.Number > cond.Value,
            ConditionOp.Gte => v.Number >= cond.Value,
            ConditionOp.Eq => v.Number == cond.Value,
            ConditionOp.Neq => v.Number != cond.Value,
            ConditionOp.Between => v.Number >= cond.Value && v.Number <= cond.Value2,
            ConditionOp.IsTrue => v.Flag == true,
            ConditionOp.IsFalse => v.Flag == false,
            ConditionOp.In => cond.Values.Contains(v.Text!, StringComparer.OrdinalIgnoreCase),
            ConditionOp.NotIn => !cond.Values.Contains(v.Text!, StringComparer.OrdinalIgnoreCase),
            ConditionOp.AnyLotIn => v.Lots!.Any(l => cond.Values.Contains(l, StringComparer.OrdinalIgnoreCase)),
            _ => false
        };
    }

    private static void ScorePercentile(IReadOnlyList<CandidateSite> sites, SiteScoreResult[] results, int c, Criterion crit, FieldCatalog catalog)
    {
        var field = catalog.Find(crit.Field)!;
        var max = crit.MaxPoints ?? 0;
        foreach (var seg in Enumerable.Range(0, sites.Count).GroupBy(i => sites[i].Segment))
        {
            var withData = new List<(int Index, double Value)>();
            foreach (var i in seg)
            {
                var v = field.Read(sites[i]).Number;
                if (v is null) { results[i].Points[c] = crit.Missing.Award; results[i].Missing[c] = true; }
                else withData.Add((i, v.Value));
            }
            var pct = Percentiles(withData.Select(x => x.Value).ToArray());
            for (var k = 0; k < withData.Count; k++) results[withData[k].Index].Points[c] = max * pct[k];
        }
    }

    /// <summary>Percentile rank in (0, 1] using the average rank of ties (same as pandas <c>rank(pct=True)</c>).</summary>
    public static double[] Percentiles(double[] values)
    {
        var n = values.Length;
        var order = Enumerable.Range(0, n).OrderBy(i => values[i]).ToArray();
        var result = new double[n];
        var start = 0;
        while (start < n)
        {
            var end = start;
            while (end + 1 < n && values[order[end + 1]] == values[order[start]]) end++;
            var avgRank = (start + 1 + end + 1) / 2.0;
            for (var k = start; k <= end; k++) result[order[k]] = avgRank / n;
            start = end + 1;
        }
        return result;
    }

    /// <summary>Theoretical [min, max] total over the enabled criteria (each criterion's lowest and highest award).</summary>
    public static (double Min, double Max) PossibleRange(MethodologyRules rules, FieldCatalog? catalog = null)
    {
        double lo = 0, hi = 0;
        foreach (var c in rules.Criteria.Where(c => c.Enabled))
        {
            var options = new List<double>();
            switch (c.Kind)
            {
                case CriterionKind.Linear:
                case CriterionKind.Percentile:
                    options.Add(0); options.Add(c.MaxPoints ?? 0); break;
                case CriterionKind.Cases:
                    options.AddRange(c.Cases.Select(x => x.Points)); options.Add(c.DefaultPoints); break;
            }
            if (UsesMissableField(c, catalog)) options.Add(c.Missing.Award);
            lo += options.Min(); hi += options.Max();
        }
        return (lo, hi);
    }

    public static IEnumerable<string> FieldsUsed(Criterion c) =>
        c.Kind == CriterionKind.Cases
            ? c.Cases.SelectMany(x => x.Conditions).Select(x => x.Field).Distinct()
            : c.Field is null ? [] : [c.Field];

    public static bool UsesMissableField(Criterion c, FieldCatalog? catalog = null) =>
        FieldsUsed(c).Any(f => (catalog ?? FieldCatalog.Builtin).Find(f)?.CanBeMissing == true);
}
