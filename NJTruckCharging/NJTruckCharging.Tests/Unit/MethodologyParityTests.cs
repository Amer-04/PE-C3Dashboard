using NJTruckCharging.Domain.Entities;
using NJTruckCharging.Domain.Methodology;
using NJTruckCharging.Domain.Scoring;
using NJTruckCharging.Infrastructure.Excel;

namespace NJTruckCharging.Tests.Unit;

/// <summary>Loads and scores the real 30,584-site workbook once (the same steps as ImportService).</summary>
public sealed class RealWorkbookFixture
{
    public static readonly string WorkbookPath =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "data", "C3_candidate_sites_all_segments.xlsx"));

    public List<CandidateSite> Sites { get; }

    public RealWorkbookFixture()
    {
        using var fs = File.OpenRead(WorkbookPath);
        var wb = new CandidateWorkbookReader().Read(fs);
        foreach (var s in wb.Sites) s.RefreshDerivedFlags();
        SiteScorer.ScoreAll(wb.Sites);
        foreach (var s in wb.Sites)
            if (SiteScorer.InferBusyMultiTenant(s, s.FileScore)) s.BusyMultiTenant = true;
        SiteScorer.ScoreAll(wb.Sites);
        Sites = wb.Sites;
    }
}

/// <summary>
/// Step-1 acceptance: the configurable engine running baseline Version 1 reproduces the original engine and the
/// workbook — every score and every rank, including the 4,356 sites in exact (score, distance) ties.
/// </summary>
public class MethodologyParityTests(RealWorkbookFixture fx) : IClassFixture<RealWorkbookFixture>
{
    [Fact]
    public void Workbook_has_all_30584_candidates()
    {
        Assert.Equal(30_584, fx.Sites.Count);
        Assert.Equal(fx.Sites.Count, fx.Sites.Select(s => s.SiteId).Distinct().Count());
    }

    [Fact]
    public void Baseline_rules_are_valid()
    {
        var issues = MethodologyValidator.Validate(BaselineMethodology.Create());
        Assert.DoesNotContain(issues, i => i.Severity == IssueSeverity.Error);
    }

    [Fact]
    public void Baseline_engine_reproduces_every_score_rank_and_breakdown_for_all_sites()
    {
        var result = MethodologyEngine.Evaluate(fx.Sites, BaselineMethodology.Create());
        var scoreMismatch = 0; var rankMismatch = 0; var workbookRankMismatch = 0; var breakdownMismatch = 0;
        for (var i = 0; i < fx.Sites.Count; i++)
        {
            var s = fx.Sites[i];
            var r = result.Sites[i];
            if (r.Score != s.Score) scoreMismatch++;
            if (r.Rank != s.ComputedRank) rankMismatch++;
            if (r.Rank != s.RankInSegment) workbookRankMismatch++;
            foreach (var (key, column) in BaselineMethodology.BreakdownColumns)
            {
                var expected = (double)typeof(ScoreBreakdown).GetProperty(column)!.GetValue(s.Breakdown)!;
                if (Math.Abs(result.PointsFor(r, key) - expected) > 1e-9) { breakdownMismatch++; break; }
            }
        }
        Assert.Equal(0, scoreMismatch);
        Assert.Equal(0, rankMismatch);
        Assert.Equal(0, workbookRankMismatch);
        Assert.Equal(0, breakdownMismatch);
        // Scores also match the workbook's delivered values (±0.05 rounding of the source).
        Assert.True(fx.Sites.Zip(result.Sites).Max(p => Math.Abs(p.Second.Score - p.First.FileScore)) <= 0.06);
    }

    [Fact]
    public void Ties_exist_in_real_data_and_rank_identically_regardless_of_input_order()
    {
        var tied = fx.Sites.GroupBy(s => (s.Segment, s.Score, s.MilesToExit)).Where(g => g.Count() > 1).Sum(g => g.Count());
        Assert.True(tied > 4000, $"expected thousands of exact ties, found {tied}");

        var rules = BaselineMethodology.Create();
        var inOrder = MethodologyEngine.Evaluate(fx.Sites, rules).Sites.ToDictionary(r => r.SiteId, r => r.Rank);
        var shuffled = fx.Sites.OrderBy(_ => Random.Shared.Next()).ToList();
        var reordered = MethodologyEngine.Evaluate(shuffled, rules).Sites.ToDictionary(r => r.SiteId, r => r.Rank);
        Assert.All(inOrder, kv => Assert.Equal(kv.Value, reordered[kv.Key]));

        // The original engine is now order-independent too.
        SiteScorer.ScoreAll(shuffled);
        Assert.All(shuffled, s => Assert.Equal(s.RankInSegment, s.ComputedRank));
    }

    [Fact]
    public void Ranks_are_a_permutation_within_each_segment()
    {
        var result = MethodologyEngine.Evaluate(fx.Sites, BaselineMethodology.Create());
        foreach (var g in result.Sites.GroupBy(r => r.Segment))
            Assert.Equal(Enumerable.Range(1, g.Count()), g.Select(r => r.Rank).OrderBy(x => x));
    }

    [Fact]
    public void Full_dataset_scores_in_well_under_two_seconds()
    {
        var rules = BaselineMethodology.Create();
        MethodologyEngine.Evaluate(fx.Sites, rules); // warm-up
        var sw = System.Diagnostics.Stopwatch.StartNew();
        MethodologyEngine.Evaluate(fx.Sites, rules);
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < 2000, $"took {sw.ElapsedMilliseconds} ms");
    }
}
