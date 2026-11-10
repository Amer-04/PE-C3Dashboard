using System.Text.Json;
using NJTruckCharging.Domain.Entities;
using NJTruckCharging.Domain.Methodology;
using NJTruckCharging.Domain.Scoring;

namespace NJTruckCharging.Application.Methodology;

/// <summary>Points of one site under one version, decoded from <see cref="SiteVersionScore.PointsJson"/>.</summary>
public sealed record StoredPoints(double[] P, int[] M)
{
    public static StoredPoints Parse(string json) =>
        JsonSerializer.Deserialize<StoredPoints>(json, Json) ?? new StoredPoints([], []);

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
}

/// <summary>Converts engine results into persisted rows and applies them to in-memory sites.</summary>
public static class MethodologyScoring
{
    public static List<SiteVersionScore> ToRows(int versionId, MethodologyRules rules, MethodologyResult result)
    {
        var demandIdx = Indexes(rules, c => c.Category == CriterionCategory.TruckDemand);
        var builtIn = BuiltInIndexes(rules);
        var rows = new List<SiteVersionScore>(result.Sites.Count);
        foreach (var r in result.Sites)
        {
            rows.Add(new SiteVersionScore
            {
                MethodologyVersionId = versionId,
                SiteId = r.SiteId,
                Segment = r.Segment,
                Score = r.Score,
                Rank = r.Rank,
                TruckDemandPoints = Sum(r.Points, demandIdx),
                Breakdown = Breakdown(r.Points, builtIn),
                PointsJson = JsonSerializer.Serialize(
                    new StoredPoints(r.Points.Select(p => Math.Round(p, 6)).ToArray(),
                        Enumerable.Range(0, r.Missing.Length).Where(i => r.Missing[i]).ToArray()), StoredPoints.Json)
            });
        }
        return rows;
    }

    /// <summary>Writes a version's results onto the live columns of in-memory sites (used by the re-import).</summary>
    public static void ApplyToSites(IReadOnlyList<CandidateSite> sites, int? versionId, MethodologyRules rules, MethodologyResult result)
    {
        var demandIdx = Indexes(rules, c => c.Category == CriterionCategory.TruckDemand);
        var builtIn = BuiltInIndexes(rules);
        var byId = result.Sites.ToDictionary(r => r.SiteId, StringComparer.Ordinal);
        foreach (var s in sites)
        {
            var r = byId[s.SiteId];
            s.Score = r.Score;
            s.ComputedRank = r.Rank;
            s.Breakdown = Breakdown(r.Points, builtIn);
            s.TruckDemandPoints = Sum(r.Points, demandIdx);
            s.ScoreVersionId = versionId;
        }
    }

    private static int[] Indexes(MethodologyRules rules, Func<Criterion, bool> pred) =>
        Enumerable.Range(0, rules.Criteria.Count).Where(i => pred(rules.Criteria[i])).ToArray();

    private static (int Index, System.Reflection.PropertyInfo Prop)[] BuiltInIndexes(MethodologyRules rules) =>
        Enumerable.Range(0, rules.Criteria.Count)
            .Where(i => BaselineMethodology.BreakdownColumns.ContainsKey(rules.Criteria[i].Key))
            .Select(i => (i, typeof(ScoreBreakdown).GetProperty(BaselineMethodology.BreakdownColumns[rules.Criteria[i].Key])!))
            .ToArray();

    private static double Sum(double[] points, int[] idx)
    {
        double t = 0;
        foreach (var i in idx) t += points[i];
        return t;
    }

    private static ScoreBreakdown Breakdown(double[] points, (int Index, System.Reflection.PropertyInfo Prop)[] builtIn)
    {
        var b = new ScoreBreakdown();
        foreach (var (i, prop) in builtIn) prop.SetValue(b, points[i]);
        return b;
    }
}

/// <summary>The currently active methodology, cached in memory (refreshed on startup, publish and re-import).</summary>
public sealed record ActiveMethodology(
    int VersionId, string Name, bool IsBaseline, MethodologyRules Rules, string RulesHash,
    DateTimeOffset? ActivatedAtUtc, double MinPossible, double MaxPossible);

public sealed class ActiveMethodologyCache
{
    private volatile ActiveMethodology? _current;
    public ActiveMethodology? Current => _current;
    public void Set(ActiveMethodology? value) => _current = value;
    /// <summary>"v3 · Name" for CSV/exports; empty before initialisation.</summary>
    public string Label => _current is { } a ? $"v{a.VersionId} · {a.Name}" : "";
}
