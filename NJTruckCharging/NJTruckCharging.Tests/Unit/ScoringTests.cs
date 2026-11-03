using NJTruckCharging.Domain.Entities;
using NJTruckCharging.Domain.Scoring;

namespace NJTruckCharging.Tests.Unit;

/// <summary>Unit tests for the C3 scoring engine (pure domain logic, no database).</summary>
public class ScoringTests
{
    private static readonly ScoringRules R = ScoringRules.Default;

    [Theory]
    [InlineData(0.0, 20)]     // at the exit
    [InlineData(0.5, 20)]     // full-point threshold
    [InlineData(5.0, 0)]      // zero at 5 mi
    [InlineData(10.0, 0)]     // clamped, never negative
    public void ExitDistance_is_linear_between_thresholds(double miles, double expected)
    {
        Assert.Equal(expected, SiteScorer.ExitDistancePoints(miles, R), 3);
    }

    [Fact]
    public void ExitDistance_midpoint_is_half()
    {
        // halfway (2.75 mi) between 0.5 and 5.0 → ~half of 20
        Assert.Equal(10, SiteScorer.ExitDistancePoints(2.75, R), 1);
    }

    [Theory]
    [InlineData(3.0, 20)]
    [InlineData(1.5, 16)]
    [InlineData(1.0, 10)]
    [InlineData(0.6, 3)]
    [InlineData(0.3, -15)]    // tiny lots are penalised
    public void OpenLand_tiers(double acres, double expected) =>
        Assert.Equal(expected, SiteScorer.OpenLandPoints(acres, R));

    [Theory]
    [InlineData("4B", 15)]    // industrial
    [InlineData("1", 11)]     // vacant
    [InlineData("4A", 8)]     // commercial
    [InlineData("1/4B", 15)]  // merged site → best class wins
    [InlineData("4A/1", 11)]
    public void PropertyClass_uses_best_class(string cls, double expected) =>
        Assert.Equal(expected, SiteScorer.PropertyClassPoints(cls, R));

    [Theory]
    [InlineData(0.6, 200, 5)]    // compact + wide → good
    [InlineData(0.6, 100, -10)]  // too narrow → penalty
    [InlineData(0.3, 200, 0)]    // not compact enough, but wide → neutral
    public void LotShape_rewards_wide_compact_lots(double compactness, double shortSide, double expected) =>
        Assert.Equal(expected, SiteScorer.LotShapePoints(compactness, shortSide, R));

    [Fact]
    public void Flood_penalty_applies_to_unchecked_sites_by_default()
    {
        var s = NewSite();
        s.FloodZone100Yr = null; // not checked
        s.RefreshDerivedFlags();
        var b = SiteScorer.ScoreFixedComponents(s, R);
        Assert.Equal(R.FloodPenalty, b.Flood);
    }

    [Fact]
    public void Flood_penalty_absent_when_checked_and_clear()
    {
        var s = NewSite();
        s.FloodZone100Yr = "none"; // checked, clear
        s.RefreshDerivedFlags();
        var b = SiteScorer.ScoreFixedComponents(s, R);
        Assert.Equal(0, b.Flood);
    }

    [Fact]
    public void ScoreSegment_ranks_by_score_then_distance()
    {
        var high = NewSite(openAcres: 3, exit: 0.1);   // more open land + closer exit
        var low = NewSite(openAcres: 0.3, exit: 4);
        var sites = new List<CandidateSite> { low, high };

        SiteScorer.ScoreSegment(sites, R);

        Assert.True(high.Score > low.Score);
        Assert.Equal(1, high.ComputedRank);
        Assert.Equal(2, low.ComputedRank);
    }

    [Fact]
    public void Percentiles_rank_highest_value_at_one()
    {
        var sites = new[] { NewSite(trucks: 1), NewSite(trucks: 5), NewSite(trucks: 9) };
        var pct = SiteScorer.Percentiles(sites, s => s.TrucksBased5Mi);
        Assert.Equal(1.0, pct[2], 3);           // the max is at the 100th percentile
        Assert.True(pct[0] < pct[1] && pct[1] < pct[2]);
    }

    private static CandidateSite NewSite(double openAcres = 1, double exit = 1, int trucks = 0) => new()
    {
        SiteId = Guid.NewGuid().ToString("N"), PamsPin = "x", Segment = 1,
        SiteAddress = "1 Test St", Municipality = "Testville", County = "Union",
        PropClass = "4B", OpenAcresEst = openAcres, MilesToExit = exit, TrucksBased5Mi = trucks,
        FloodZone100Yr = "none",
    };
}
