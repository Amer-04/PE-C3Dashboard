using NJTruckCharging.Application.Comparison;
using NJTruckCharging.Application.Sites;
using NJTruckCharging.Domain.Entities;

namespace NJTruckCharging.Tests.Unit;

/// <summary>Unit tests for the centralised IOS rules and comparison input validation (no database).</summary>
public class IosRulesTests
{
    private static CandidateSite Site(string cls, double open, bool freight, string id = "S1") =>
        new() { SiteId = id, PamsPin = "p", SiteAddress = "a", Municipality = "m", County = "c", PropClass = cls, OpenAcresEst = open, FreightUse = freight };

    [Theory]
    [InlineData("4B", 0.0, true, IosClassification.Unknown)]           // no open-acre estimate
    [InlineData("4A", 3.0, true, IosClassification.FreightIndicated)]  // freight signal, NOT confirmed
    [InlineData("4B", 3.0, true, IosClassification.FreightIndicated)]
    [InlineData("4B", 3.0, false, IosClassification.Potential)]
    [InlineData("1/4A", 1.0, false, IosClassification.Potential)]
    [InlineData("4A", 3.0, false, IosClassification.NotIos)]           // commercial
    [InlineData("4B", 0.5, true, IosClassification.NotIos)]            // too little open land
    public void Derived_classification_follows_the_rule(string cls, double open, bool freight, IosClassification expected) =>
        Assert.Equal(expected, IosRules.Classify(Site(cls, open, freight)));

    [Fact]
    public void Source_data_alone_never_yields_Confirmed()
    {
        foreach (var cls in new[] { "1", "4A", "4B", "1/4B" })
            foreach (var open in new[] { 0.0, 0.5, 1.0, 50.0 })
                foreach (var freight in new[] { true, false })
                {
                    var a = IosRules.Assess(Site(cls, open, freight), review: null);
                    Assert.NotEqual(IosClassification.Confirmed, a.Classification);
                    Assert.Equal(IosBasis.SourceDerived, a.Basis);
                    Assert.Contains("not manually verified", a.Evidence);
                }
    }

    [Fact]
    public void Manual_review_overrides_and_is_labelled_as_manual()
    {
        var s = Site("4A", 3, freight: false); // derived NotIos
        var verified = IosRules.Assess(s, new FeasibilityItem { SiteId = "S1", ItemKey = FeasibilityItemKey.OutdoorStorageUse, Status = FeasibilityStatus.Verified, Evidence = "40 trailers stored in yard" });
        Assert.Equal(IosClassification.Confirmed, verified.Classification);
        Assert.Equal(IosBasis.ManualReview, verified.Basis);
        Assert.Equal(IosClassification.NotIos, verified.DerivedClassification);
        Assert.Contains("40 trailers", verified.Evidence);

        var concern = IosRules.Assess(Site("4B", 5, true), new FeasibilityItem { Status = FeasibilityStatus.Concern, Evidence = "Fully paved retail lot" });
        Assert.Equal(IosClassification.NotIos, concern.Classification);
        Assert.Equal(IosBasis.ManualReview, concern.Basis);

        // An "Unknown" review row is not a review: stays source-derived.
        var unknown = IosRules.Assess(Site("4B", 5, true), new FeasibilityItem { Status = FeasibilityStatus.Unknown });
        Assert.Equal(IosClassification.FreightIndicated, unknown.Classification);
        Assert.Equal(IosBasis.SourceDerived, unknown.Basis);
    }

    [Fact]
    public void Sql_predicate_matches_in_memory_classification()
    {
        // Same expression tree used for SQL; evaluated here with LINQ-to-objects over every rule branch.
        var sites = new List<CandidateSite>();
        var i = 0;
        foreach (var cls in new[] { "1", "4A", "4B", "1/4B", "4A/4B" })
            foreach (var open in new[] { 0.0, 0.9, 1.0, 7.5 })
                foreach (var freight in new[] { true, false })
                    sites.Add(Site(cls, open, freight, $"S{i++}"));
        var reviews = new List<FeasibilityItem>
        {
            new() { SiteId = "S3", ItemKey = FeasibilityItemKey.OutdoorStorageUse, Status = FeasibilityStatus.Verified, Evidence = "yard" },
            new() { SiteId = "S5", ItemKey = FeasibilityItemKey.OutdoorStorageUse, Status = FeasibilityStatus.Concern },
            new() { SiteId = "S6", ItemKey = FeasibilityItemKey.ElectricalService, Status = FeasibilityStatus.Verified }, // other key: ignored
        };
        var byId = reviews.Where(r => r.ItemKey == IosRules.ReviewKey).ToDictionary(r => r.SiteId);

        foreach (var c in Enum.GetValues<IosClassification>())
        {
            var predicate = IosRules.InClasses([c], reviews.AsQueryable()).Compile();
            var viaPredicate = sites.Where(predicate).Select(s => s.SiteId).ToHashSet();
            var viaAssess = sites.Where(s => IosRules.Assess(s, byId.GetValueOrDefault(s.SiteId)).Classification == c)
                .Select(s => s.SiteId).ToHashSet();
            Assert.Equal(viaAssess, viaPredicate);
        }
    }

    [Theory]
    [InlineData("confirmed", IosClassification.Confirmed)]
    [InlineData("indicated", IosClassification.FreightIndicated)]
    [InlineData("POTENTIAL", IosClassification.Potential)]
    [InlineData("not", IosClassification.NotIos)]
    [InlineData("unknown", IosClassification.Unknown)]
    public void Filter_tokens_parse(string token, IosClassification expected) =>
        Assert.Equal([expected], IosRules.ParseTokens([token]));

    [Theory]
    [InlineData(new[] { "A" }, true)]
    [InlineData(new[] { "A", "a" }, true)]             // duplicates collapse → only 1
    [InlineData(new[] { "A", "B" }, false)]
    [InlineData(new[] { "A,B,C" }, false)]             // comma list accepted
    [InlineData(new[] { "A", "B", "C", "D", "E" }, false)]
    [InlineData(new[] { "A", "B", "C", "D", "E", "F" }, true)]
    public void Comparison_requires_two_to_five_distinct_sites(string[] ids, bool isError) =>
        Assert.Equal(isError, SiteComparisonService.Normalise(ids).Error is not null);
}
