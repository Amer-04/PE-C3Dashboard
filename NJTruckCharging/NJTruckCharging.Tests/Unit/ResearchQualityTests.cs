using NJTruckCharging.Application.Research;
using NJTruckCharging.Domain.Entities;

namespace NJTruckCharging.Tests.Unit;

public class ResearchQualityTests
{
    private static readonly DateOnly D = new(2026, 10, 1);

    private static FeasibilityItem F(FeasibilityItemKey key, FeasibilityStatus status, bool documented = true, string? evidence = "seen") => new()
    {
        SiteId = "S", ItemKey = key, Status = status, Evidence = evidence,
        SourceUrl = documented ? "https://example.org/src" : null, EvidenceDate = documented ? D : null,
        Reviewer = documented ? "Tester" : null
    };

    private static SiteContact C(ContactVerificationStatus status, bool sourced = true, ContactType type = ContactType.PropertyOwner) =>
        new() { SiteId = "S", Name = "N", ContactType = type, VerificationStatus = status, SourceUrl = sourced ? "https://example.org/c" : null };

    [Fact]
    public void Missing_item_is_not_started() => Assert.Equal(CheckState.NotStarted, ResearchQuality.State(null));

    [Fact]
    public void Documented_verified_is_verified() =>
        Assert.Equal(CheckState.Verified, ResearchQuality.State(F(FeasibilityItemKey.Zoning, FeasibilityStatus.Verified)));

    [Theory]
    [InlineData(false, "seen")]   // no source / date (e.g. a record made before Phase 1)
    [InlineData(true, null)]      // no evidence text
    public void Verified_without_full_documentation_is_insufficient_not_verified(bool documented, string? evidence)
    {
        var state = ResearchQuality.State(F(FeasibilityItemKey.Zoning, FeasibilityStatus.Verified, documented, evidence));
        Assert.Equal(CheckState.InsufficientEvidence, state);
        Assert.False(ResearchQuality.IsReviewed(state));
    }

    [Fact]
    public void Documented_concern_counts_as_reviewed_but_never_as_verified()
    {
        var states = ResearchQuality.States([F(FeasibilityItemKey.Zoning, FeasibilityStatus.Concern)], []);
        Assert.Equal(CheckState.Concern, states[ResearchCheck.Zoning]);
        Assert.Equal((1, 0), ResearchQuality.Counts(states));
    }

    [Fact]
    public void Conflicting_is_neither_reviewed_nor_verified()
    {
        var states = ResearchQuality.States([F(FeasibilityItemKey.Zoning, FeasibilityStatus.Conflicting)], []);
        Assert.Equal(CheckState.Conflicting, states[ResearchCheck.Zoning]);
        Assert.Equal((0, 0), ResearchQuality.Counts(states));
    }

    [Fact]
    public void Contact_check_needs_a_verified_contact_with_a_source()
    {
        Assert.Equal(CheckState.NotStarted, ResearchQuality.ContactState([]));
        Assert.Equal(CheckState.NotStarted, ResearchQuality.ContactState([C(ContactVerificationStatus.Unverified)]));
        Assert.Equal(CheckState.InsufficientEvidence, ResearchQuality.ContactState([C(ContactVerificationStatus.Verified, sourced: false)]));
        Assert.Equal(CheckState.Verified, ResearchQuality.ContactState([C(ContactVerificationStatus.Verified, type: ContactType.Other)]));
    }

    [Fact]
    public void Non_core_items_do_not_count_toward_completeness()
    {
        var states = ResearchQuality.States([F(FeasibilityItemKey.FloodEnvironmental, FeasibilityStatus.Verified)], []);
        Assert.Equal((0, 0), ResearchQuality.Counts(states));
        Assert.Equal(8, states.Count);
    }

    [Fact]
    public void All_eight_reviewed_is_complete()
    {
        var items = ResearchQuality.CheckItems.Values.Select(k => F(k, k == FeasibilityItemKey.Zoning ? FeasibilityStatus.Concern : FeasibilityStatus.Verified)).ToList();
        var (reviewed, verified) = ResearchQuality.Counts(ResearchQuality.States(items, [C(ContactVerificationStatus.Verified)]));
        Assert.Equal(8, reviewed);
        Assert.Equal(7, verified);
        Assert.Equal(ResearchStage.AllReviewed, ResearchQuality.Stage(reviewed));
    }

    [Theory]
    [InlineData(0, ResearchStage.NoneReviewed)]
    [InlineData(1, ResearchStage.PartlyReviewed)]
    [InlineData(7, ResearchStage.PartlyReviewed)]
    [InlineData(8, ResearchStage.AllReviewed)]
    public void Stage_boundaries(int reviewed, ResearchStage expected) => Assert.Equal(expected, ResearchQuality.Stage(reviewed));

    [Fact]
    public void Untouched_research_site_has_truck_access_and_electrical_issues_only()
        => Assert.Equal([ReviewIssue.TruckAccessUnchecked, ReviewIssue.ElectricalUnknown], ResearchQuality.Issues([], []));

    [Theory]
    [InlineData(FeasibilityItemKey.ParcelIdentity, FeasibilityStatus.Concern, ReviewIssue.ParcelMismatch)]
    [InlineData(FeasibilityItemKey.ParcelIdentity, FeasibilityStatus.Conflicting, ReviewIssue.ParcelMismatch)]
    [InlineData(FeasibilityItemKey.BusinessAtAddress, FeasibilityStatus.Concern, ReviewIssue.BusinessConflict)]
    [InlineData(FeasibilityItemKey.BusinessAtAddress, FeasibilityStatus.Conflicting, ReviewIssue.BusinessConflict)]
    [InlineData(FeasibilityItemKey.Zoning, FeasibilityStatus.Conflicting, ReviewIssue.ConflictingEvidence)]
    public void Discrepancies_raise_issues(FeasibilityItemKey key, FeasibilityStatus status, ReviewIssue expected)
        => Assert.Contains(expected, ResearchQuality.Issues([F(key, status)], []));

    [Fact]
    public void Verified_parcel_and_business_raise_no_discrepancy()
    {
        var issues = ResearchQuality.Issues([F(FeasibilityItemKey.ParcelIdentity, FeasibilityStatus.Verified),
                                              F(FeasibilityItemKey.BusinessAtAddress, FeasibilityStatus.Verified)], []);
        Assert.DoesNotContain(ReviewIssue.ParcelMismatch, issues);
        Assert.DoesNotContain(ReviewIssue.BusinessConflict, issues);
        Assert.DoesNotContain(ReviewIssue.InsufficientEvidence, issues);
    }

    [Fact]
    public void Checked_access_and_electrical_clear_those_issues_even_as_concerns()
    {
        var issues = ResearchQuality.Issues([F(FeasibilityItemKey.TruckEntranceAccess, FeasibilityStatus.Concern),
                                              F(FeasibilityItemKey.ElectricalService, FeasibilityStatus.Verified)], []);
        Assert.Empty(issues);
    }

    [Fact]
    public void Unverified_contact_and_unsourced_verdicts_are_flagged()
    {
        Assert.Contains(ReviewIssue.UnverifiedContact, ResearchQuality.Issues([], [C(ContactVerificationStatus.Unverified)]));
        Assert.Contains(ReviewIssue.UnverifiedContact, ResearchQuality.Issues([], [C(ContactVerificationStatus.Verified, sourced: false)]));
        Assert.DoesNotContain(ReviewIssue.UnverifiedContact, ResearchQuality.Issues([], [C(ContactVerificationStatus.Verified)]));
        Assert.Contains(ReviewIssue.InsufficientEvidence,
            ResearchQuality.Issues([F(FeasibilityItemKey.FloodEnvironmental, FeasibilityStatus.Verified, documented: false)], []));
    }

    // ---- Site Evidence and Site Details follow the same documented-evidence rule ----

    private static FeasibilityItem Parcel(FeasibilityStatus status, string? evidence = "Tax map matches", string? source = "https://example.org/map",
        DateOnly? date = null, bool noDate = false, string? reviewer = "Tester") => new()
    {
        SiteId = "S", ItemKey = FeasibilityItemKey.ParcelIdentity, Status = status, Evidence = evidence,
        SourceUrl = source, EvidenceDate = noDate ? null : date ?? D, Reviewer = reviewer
    };

    private static NJTruckCharging.Application.Sites.SiteEvidence Ev(FeasibilityItem f) =>
        NJTruckCharging.Application.Sites.ResearchReadiness.Evidence([f], ScreeningDecision.NotReviewed);

    private static NJTruckCharging.Application.Sites.SuitabilityItemDto Identity(FeasibilityItem f) =>
        NJTruckCharging.Application.Sites.SiteSuitability.Build(new CandidateSite { SiteId = "S", SiteAddress = "1 A St", Municipality = "M", County = "C", PropClass = "4B" },
            new Dictionary<FeasibilityItemKey, FeasibilityItem> { [f.ItemKey] = f }, []).Items.Single(i => i.Key == "Identity");

    [Fact]
    public void Missing_source_is_insufficient_evidence_not_verified()
    {
        var f = Parcel(FeasibilityStatus.Verified, source: null);
        Assert.Equal(NJTruckCharging.Application.Sites.SiteEvidence.InsufficientEvidence, Ev(f));
        Assert.Equal(CheckState.InsufficientEvidence, ResearchQuality.State(f));
        Assert.Equal(NJTruckCharging.Application.Sites.SuitabilityBasis.InsufficientEvidence, Identity(f).Basis);
        Assert.Contains("a source URL", ResearchQuality.MissingText(f));
    }

    [Fact]
    public void Missing_evidence_date_is_insufficient_evidence_not_verified()
    {
        var f = Parcel(FeasibilityStatus.Verified, noDate: true);
        Assert.Equal(NJTruckCharging.Application.Sites.SiteEvidence.InsufficientEvidence, Ev(f));
        Assert.Equal(CheckState.InsufficientEvidence, ResearchQuality.State(f));
        Assert.Equal("an evidence date", ResearchQuality.MissingText(f));
    }

    [Fact]
    public void Missing_recorded_by_name_is_insufficient_evidence_not_verified()
    {
        foreach (var name in new string?[] { null, "" })
        {
            var f = Parcel(FeasibilityStatus.Verified, reviewer: name);
            Assert.False(ResearchQuality.IsDocumented(f));
            Assert.Equal(NJTruckCharging.Application.Sites.SiteEvidence.InsufficientEvidence, Ev(f));
            Assert.Equal(CheckState.InsufficientEvidence, ResearchQuality.State(f));
            Assert.Equal(NJTruckCharging.Application.Sites.SuitabilityBasis.InsufficientEvidence, Identity(f).Basis);
            Assert.Equal("a Recorded-by name", ResearchQuality.MissingText(f));
        }
    }

    [Fact]
    public void Concern_without_recorded_by_name_is_not_counted_as_reviewed()
    {
        var f = Parcel(FeasibilityStatus.Concern, reviewer: null);
        Assert.Equal(CheckState.InsufficientEvidence, ResearchQuality.State(f));
        Assert.Equal(NJTruckCharging.Application.Sites.SiteEvidence.Mismatch, Ev(f));   // a discrepancy is never hidden
        Assert.Equal((0, 0), ResearchQuality.Counts(ResearchQuality.States([f], [])));
    }

    [Fact]
    public void Legacy_record_without_source_or_date_is_insufficient_evidence()
    {
        var f = Parcel(FeasibilityStatus.Verified, source: null, noDate: true, reviewer: null);
        Assert.Equal(NJTruckCharging.Application.Sites.SiteEvidence.InsufficientEvidence, Ev(f));
        Assert.Equal("a source URL, an evidence date and a Recorded-by name", ResearchQuality.MissingText(f));
        Assert.Equal(FeasibilityStatus.Verified, f.Status);   // the record itself is untouched
        Assert.Contains(ReviewIssue.InsufficientEvidence, ResearchQuality.Issues([f], []));
    }

    [Fact]
    public void Conflicting_parcel_is_a_mismatch_and_never_verified()
    {
        var f = Parcel(FeasibilityStatus.Conflicting, evidence: "Map vs deed disagree");
        Assert.Equal(NJTruckCharging.Application.Sites.SiteEvidence.Mismatch, Ev(f));
        Assert.Equal(NJTruckCharging.Application.Sites.SuitabilityBasis.Conflicting, Identity(f).Basis);
        Assert.NotEqual(NJTruckCharging.Application.Sites.SuitabilityAnswer.Yes, Identity(f).Answer);
    }

    [Fact]
    public void Documented_verification_is_verified_everywhere()
    {
        var f = Parcel(FeasibilityStatus.Verified);
        Assert.Equal(NJTruckCharging.Application.Sites.SiteEvidence.Verified, Ev(f));
        Assert.Equal(CheckState.Verified, ResearchQuality.State(f));
        Assert.Equal(NJTruckCharging.Application.Sites.SuitabilityBasis.Verified, Identity(f).Basis);
        Assert.Equal("", ResearchQuality.MissingText(f));
    }

    [Fact]
    public void Tokens_parse()
    {
        var (any, stages) = ResearchQuality.ParseStages(["any", "Complete", "bogus"]);
        Assert.True(any);
        Assert.Equal([ResearchStage.AllReviewed], stages);
        Assert.Equal([ReviewIssue.ParcelMismatch, ReviewIssue.ElectricalUnknown], ResearchQuality.ParseIssues(["parcelmismatch", "ElectricalUnknown", "x"]));
    }
}
