using NJTruckCharging.Application.Sites;
using NJTruckCharging.Domain.Entities;

namespace NJTruckCharging.Tests.Unit;

/// <summary>The Site Details "at a glance" answers (pure rules, no database).</summary>
public class SiteSuitabilityTests
{
    private static CandidateSite Site(Action<CandidateSite>? tweak = null)
    {
        var s = new CandidateSite
        {
            SiteId = "S1", PamsPin = "p", SiteAddress = "100 Bayway Ave", Municipality = "Elizabeth", County = "Union",
            Block = "749", Lot = "1", PropClass = "4B", Acres = 10, RecordedAcres = 9.5, OpenAcresEst = 3,
            MilesToExit = 0.6, NearestHighway = "I 95/NJ Turnpike", NearestExit = "13", ShortSideFt = 400,
            Utility = "PSE&G", OwnerMailAddress = "PO Box 1", OwnerMailCity = "Newark", LotsInSite = 1,
            LandValue = 500_000, ImprovementValue = 1_000_000
        };
        tweak?.Invoke(s);
        return s;
    }

    private static readonly Dictionary<FeasibilityItemKey, FeasibilityItem> NoReview = [];

    private static Dictionary<FeasibilityItemKey, FeasibilityItem> Review(params (FeasibilityItemKey Key, FeasibilityStatus Status, string? Evidence)[] r) =>
        r.ToDictionary(x => x.Key, x => new FeasibilityItem
        {
            SiteId = "S1", ItemKey = x.Key, Status = x.Status, Evidence = x.Evidence,
            SourceUrl = "https://example.org/evidence", EvidenceDate = new DateOnly(2026, 10, 1), Reviewer = "Tester"
        });

    private static SuitabilityItemDto Item(SiteSuitabilityDto d, string key) => d.Items.Single(i => i.Key == key);

    [Fact]
    public void Answers_all_eight_questions_in_order()
    {
        var d = SiteSuitability.Build(Site(), NoReview, []);
        Assert.Equal(["Identity", "Highway", "Land", "Capacity", "TruckSpaces", "Access", "Power", "Contact"], d.Items.Select(i => i.Key));
        Assert.Equal(8, d.Yes + d.NeedsCheck + d.No + d.Unknown);
    }

    [Fact]
    public void Nothing_is_verified_without_a_reviewer_verdict_or_verified_contact()
    {
        foreach (var open in new[] { 0.0, 1.0, 1.6, 50 })
            foreach (var miles in new[] { 0.2, 4.6 })
            {
                var d = SiteSuitability.Build(Site(s => { s.OpenAcresEst = open; s.MilesToExit = miles; }), NoReview, []);
                Assert.DoesNotContain(d.Items, i => i.Basis == SuitabilityBasis.Verified);
                Assert.Equal(0, d.VerifiedAnswers);
                // Truck-suitable spaces, power and contact are never answered "Yes" from source data alone.
                Assert.Equal(SuitabilityAnswer.Unknown, Item(d, "TruckSpaces").Answer);
                Assert.Equal(SuitabilityAnswer.Unknown, Item(d, "Power").Answer);
                Assert.Equal(SuitabilityAnswer.Unknown, Item(d, "Contact").Answer);
            }
    }

    [Theory]
    [InlineData("100 Bayway Ave", 10, 9.5, SuitabilityAnswer.Yes)]
    [InlineData("Advantage Ct", 10, 9.5, SuitabilityAnswer.NeedsCheck)]          // no street number
    [InlineData("Route 17 (Radio Tower)", 10, 9.5, SuitabilityAnswer.NeedsCheck)]
    [InlineData("100 Bayway Ave", 34.7, 9.1, SuitabilityAnswer.NeedsCheck)]       // mapped vs recorded acreage mismatch
    [InlineData("100 Bayway Ave", 10, 0, SuitabilityAnswer.NeedsCheck)]           // no recorded acreage to cross-check
    public void Identity_flags_possible_parcel_mismatches(string address, double acres, double recorded, SuitabilityAnswer expected)
    {
        var i = Item(SiteSuitability.Build(Site(s => { s.SiteAddress = address; s.Acres = acres; s.RecordedAcres = recorded; }), NoReview, []), "Identity");
        Assert.Equal(expected, i.Answer);
        Assert.Equal(SuitabilityBasis.SourceData, i.Basis);
        if (expected != SuitabilityAnswer.Yes) Assert.Contains("Confirm the parcel boundary", i.NextStep);
    }

    [Fact]
    public void Identity_flags_zero_assessed_and_condominium_common_land()
    {
        var zero = Item(SiteSuitability.Build(Site(s => { s.LandValue = 0; s.ImprovementValue = 0; }), NoReview, []), "Identity");
        Assert.Equal(SuitabilityAnswer.NeedsCheck, zero.Answer);
        Assert.Contains("assessed at $0", zero.Detail);
        var common = Item(SiteSuitability.Build(Site(s => { s.BuildingDescription = "COMMON ELEMENTS"; s.LandValue = 0; s.ImprovementValue = 0; }), NoReview, []), "Identity");
        Assert.Contains("common elements", common.Detail);
        Assert.DoesNotContain("assessed at $0", common.Detail); // one explanation, not two
    }

    [Theory]
    [InlineData(0.4, SuitabilityAnswer.Yes)]
    [InlineData(3.0, SuitabilityAnswer.Yes)]
    [InlineData(4.6, SuitabilityAnswer.NeedsCheck)]   // methodology: check driving distance beyond ~4 mi
    public void Highway_distance(double miles, SuitabilityAnswer expected) =>
        Assert.Equal(expected, Item(SiteSuitability.Build(Site(s => s.MilesToExit = miles), NoReview, []), "Highway").Answer);

    [Theory]
    [InlineData(3.0, SuitabilityAnswer.Yes, 45)]
    [InlineData(1.6, SuitabilityAnswer.NeedsCheck, 24)]   // 20–29 meets the minimum, below preferred
    [InlineData(1.0, SuitabilityAnswer.No, 15)]
    public void Land_and_capacity_estimates_use_the_20_30_space_target(double open, SuitabilityAnswer expected, int spaces)
    {
        var d = SiteSuitability.Build(Site(s => s.OpenAcresEst = open), NoReview, []);
        Assert.Equal(expected, Item(d, "Land").Answer);
        Assert.Equal(SuitabilityBasis.Estimate, Item(d, "Land").Basis);
        var cap = Item(d, "Capacity");
        Assert.Equal(expected, cap.Answer);
        Assert.Equal(SuitabilityBasis.Estimate, cap.Basis);
        Assert.Contains($"~{spaces} spaces (preliminary estimate)", cap.Headline);
    }

    [Fact]
    public void Unknown_parking_when_no_open_land_estimate()
    {
        var d = SiteSuitability.Build(Site(s => s.OpenAcresEst = 0), NoReview, []);
        foreach (var k in new[] { "Land", "Capacity", "TruckSpaces" })
        {
            Assert.Equal(SuitabilityAnswer.Unknown, Item(d, k).Answer);
            Assert.Equal(SuitabilityBasis.Missing, Item(d, k).Basis);
        }
    }

    [Theory]
    [InlineData("~48 marked trailer stalls counted on the paved yard", 48)]
    [InlineData("24 truck spaces along the fence", 24)]
    [InlineData("2 curb cuts; 1,200 sq ft office; 35 tractor-trailer stalls", 35)]
    [InlineData("Room for 30+ spaces", 30)]
    [InlineData("Large paved yard", null)]
    [InlineData(null, null)]
    public void Space_count_is_read_from_reviewer_evidence(string? evidence, int? expected) =>
        Assert.Equal(expected, SiteSuitability.ParseSpaceCount(evidence));

    [Fact]
    public void Verified_truck_count_answers_capacity_and_truck_spaces()
    {
        var d = SiteSuitability.Build(Site(s => s.OpenAcresEst = 0.5),
            Review((FeasibilityItemKey.TruckParkingCapacity, FeasibilityStatus.Verified, "~48 marked trailer stalls")), []);
        foreach (var k in new[] { "Capacity", "TruckSpaces" })
        {
            Assert.Equal(SuitabilityAnswer.Yes, Item(d, k).Answer);
            Assert.Equal(SuitabilityBasis.Verified, Item(d, k).Basis);
            Assert.Contains("48", Item(d, k).Headline);
        }
        // Verified without a number: not a pass — asks for the count.
        var noCount = SiteSuitability.Build(Site(), Review((FeasibilityItemKey.TruckParkingCapacity, FeasibilityStatus.Verified, "Big yard")), []);
        Assert.Equal(SuitabilityAnswer.NeedsCheck, Item(noCount, "Capacity").Answer);
        Assert.Equal(SuitabilityAnswer.Unknown, Item(noCount, "TruckSpaces").Answer);
    }

    [Fact]
    public void Truck_access_combines_entrance_and_turning_reviews()
    {
        Assert.Equal(SuitabilityAnswer.Unknown, Item(SiteSuitability.Build(Site(), NoReview, []), "Access").Answer);
        var entranceOnly = Item(SiteSuitability.Build(Site(), Review((FeasibilityItemKey.TruckEntranceAccess, FeasibilityStatus.Verified, "curb cuts")), []), "Access");
        Assert.Equal(SuitabilityAnswer.NeedsCheck, entranceOnly.Answer);
        Assert.Contains("turning", entranceOnly.NextStep);
        Assert.Equal(SuitabilityAnswer.Yes, Item(SiteSuitability.Build(Site(), Review(
            (FeasibilityItemKey.TruckEntranceAccess, FeasibilityStatus.Verified, "curb cuts"),
            (FeasibilityItemKey.TurningManeuveringSpace, FeasibilityStatus.Verified, "53-ft turn ok")), []), "Access").Answer);
        Assert.Equal(SuitabilityAnswer.No, Item(SiteSuitability.Build(Site(), Review(
            (FeasibilityItemKey.TruckEntranceAccess, FeasibilityStatus.Verified, "curb cuts"),
            (FeasibilityItemKey.TurningManeuveringSpace, FeasibilityStatus.Concern, "too tight")), []), "Access").Answer);
        Assert.Contains("narrow", Item(SiteSuitability.Build(Site(s => s.ShortSideFt = 90), NoReview, []), "Access").Detail);
    }

    [Fact]
    public void Power_is_unknown_unless_reviewed_and_utility_territory_is_not_capacity()
    {
        var unknown = Item(SiteSuitability.Build(Site(), NoReview, []), "Power");
        Assert.Equal(SuitabilityAnswer.Unknown, unknown.Answer);
        Assert.Contains("territory only", unknown.Detail);
        var concern = Item(SiteSuitability.Build(Site(), Review((FeasibilityItemKey.ElectricalService, FeasibilityStatus.Concern, "No 3-phase")), []), "Power");
        Assert.Equal(SuitabilityAnswer.No, concern.Answer);
        Assert.Equal(SuitabilityBasis.Verified, concern.Basis);
    }

    [Fact]
    public void Contact_distinguishes_verified_unverified_and_none()
    {
        var none = Item(SiteSuitability.Build(Site(), NoReview, []), "Contact");
        Assert.Equal(SuitabilityAnswer.Unknown, none.Answer);
        Assert.Contains("Daniel's Law", none.Detail);
        Assert.Contains("PO Box 1", none.Detail);

        var lead = new SiteContact { SiteId = "S1", Name = "Pat Broker", ContactType = ContactType.Broker, VerificationStatus = ContactVerificationStatus.Unverified };
        var possible = Item(SiteSuitability.Build(Site(), NoReview, [lead]), "Contact");
        Assert.Equal(SuitabilityAnswer.NeedsCheck, possible.Answer);
        Assert.Equal(SuitabilityBasis.Unverified, possible.Basis);

        var owner = new SiteContact { SiteId = "S1", Name = "Jo Owner", ContactType = ContactType.PropertyOwner, VerificationStatus = ContactVerificationStatus.Verified, SourceUrl = "https://example.org/jo" };
        var yes = Item(SiteSuitability.Build(Site(), NoReview, [owner, lead]), "Contact");
        Assert.Equal(SuitabilityAnswer.Yes, yes.Answer);
        Assert.Contains("Jo Owner (property owner)", yes.Headline);

        // Verified without a source, or a verified general company line, is not a "Yes".
        var unsourced = new SiteContact { SiteId = "S1", Name = "Jo Owner", ContactType = ContactType.PropertyOwner, VerificationStatus = ContactVerificationStatus.Verified };
        Assert.Equal(SuitabilityAnswer.NeedsCheck, Item(SiteSuitability.Build(Site(), NoReview, [unsourced]), "Contact").Answer);
        var mainLine = new SiteContact { SiteId = "S1", Name = "Main line", ContactType = ContactType.Other, VerificationStatus = ContactVerificationStatus.Verified, SourceUrl = "https://example.org" };
        var general = Item(SiteSuitability.Build(Site(), NoReview, [mainLine]), "Contact");
        Assert.Equal(SuitabilityAnswer.NeedsCheck, general.Answer);
        Assert.Contains("General business contact", general.Headline);

        var invalid = new SiteContact { SiteId = "S1", Name = "Old", ContactType = ContactType.Other, VerificationStatus = ContactVerificationStatus.Invalid };
        Assert.Equal(SuitabilityAnswer.Unknown, Item(SiteSuitability.Build(Site(), NoReview, [invalid]), "Contact").Answer);
    }

    [Fact]
    public void Remaining_to_verify_lists_next_steps_but_not_for_verified_yes_answers()
    {
        var bare = SiteSuitability.Build(Site(), NoReview, []);
        Assert.Contains(bare.RemainingToVerify, r => r.Contains("utility"));
        Assert.Contains(bare.RemainingToVerify, r => r.Contains("truck-suitable stalls"));

        var reviewed = SiteSuitability.Build(Site(), Review((FeasibilityItemKey.ElectricalService, FeasibilityStatus.Verified, "1,000 kVA available")), []);
        Assert.DoesNotContain(reviewed.RemainingToVerify, r => r.Contains("utility"));
        Assert.Equal(SuitabilityAnswer.Yes, Item(reviewed, "Power").Answer);
    }

    // ---- Preliminary business preferences ----

    private static SuitabilityItemDto Pref(SiteSuitabilityDto d, string key) => d.Preferences.Single(p => p.Key == key);

    [Fact]
    public void Preferences_cover_parking_highway_ingress_and_battery_storage()
    {
        var d = SiteSuitability.Build(Site(), NoReview, []);
        Assert.Equal(["PrefParking", "PrefHighway", "PrefIngress", "PrefBattery"], d.Preferences.Select(p => p.Key));
        Assert.All(d.Preferences, p => Assert.StartsWith("Preference:", p.Question));
        Assert.DoesNotContain(d.Preferences, p => p.Basis == SuitabilityBasis.Verified); // nothing verified without review
    }

    [Theory]
    [InlineData(3.0, SuitabilityAnswer.Yes)]        // 3 ac ≥ 2 ac parking + 0.25 ac storage
    [InlineData(2.1, SuitabilityAnswer.NeedsCheck)] // enough for 30 stalls, not much more
    [InlineData(1.0, SuitabilityAnswer.No)]
    [InlineData(0.0, SuitabilityAnswer.Unknown)]
    public void Battery_storage_room_is_an_estimate_after_preferred_parking(double open, SuitabilityAnswer expected)
    {
        var p = Pref(SiteSuitability.Build(Site(s => s.OpenAcresEst = open), NoReview, []), "PrefBattery");
        Assert.Equal(expected, p.Answer);
        Assert.NotEqual(SuitabilityBasis.Verified, p.Basis);
        Assert.Contains("0.25 ac", p.Detail);
    }

    [Theory]
    [InlineData(0.6, SuitabilityAnswer.Yes)]
    [InlineData(2.5, SuitabilityAnswer.NeedsCheck)]
    public void Highway_preference_is_within_about_one_mile(double miles, SuitabilityAnswer expected) =>
        Assert.Equal(expected, Pref(SiteSuitability.Build(Site(s => s.MilesToExit = miles), NoReview, []), "PrefHighway").Answer);

    [Fact]
    public void Ingress_and_parking_preferences_follow_the_verified_research()
    {
        var bare = SiteSuitability.Build(Site(), NoReview, []);
        Assert.Equal(SuitabilityAnswer.Unknown, Pref(bare, "PrefIngress").Answer);
        Assert.Equal(SuitabilityBasis.Estimate, Pref(bare, "PrefParking").Basis);

        var reviewed = SiteSuitability.Build(Site(), Review(
            (FeasibilityItemKey.TruckEntranceAccess, FeasibilityStatus.Verified, "curb cuts"),
            (FeasibilityItemKey.TurningManeuveringSpace, FeasibilityStatus.Verified, "53-ft turn ok"),
            (FeasibilityItemKey.TruckParkingCapacity, FeasibilityStatus.Verified, "34 truck stalls")), []);
        Assert.Equal(SuitabilityAnswer.Yes, Pref(reviewed, "PrefIngress").Answer);
        Assert.Equal(SuitabilityBasis.Verified, Pref(reviewed, "PrefIngress").Basis);
        Assert.Equal(SuitabilityAnswer.Yes, Pref(reviewed, "PrefParking").Answer);
        Assert.Equal(SuitabilityBasis.Verified, Pref(reviewed, "PrefParking").Basis);
        Assert.Equal(reviewed.Preferences.Count(p => p.Answer == SuitabilityAnswer.Yes), reviewed.PreferencesMet);
    }
}
