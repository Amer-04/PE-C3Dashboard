using NJTruckCharging.Application.Sites;
using NJTruckCharging.Domain.Entities;

namespace NJTruckCharging.Tests.Unit;

/// <summary>Site Evidence / Contact Readiness rules (pure, no database).</summary>
public class ResearchReadinessTests
{
    private static FeasibilityItem F(FeasibilityItemKey k, FeasibilityStatus s, string? ev = "checked") =>
        new() { SiteId = "S1", ItemKey = k, Status = s, Evidence = ev, SourceUrl = "https://example.org/evidence", EvidenceDate = new DateOnly(2026, 10, 1) };

    private static SiteContact C(ContactType t, ContactVerificationStatus vs, string? src = "https://src.example") =>
        new() { SiteId = "S1", Name = "N", ContactType = t, VerificationStatus = vs, SourceUrl = src };

    [Fact]
    public void Site_evidence_comes_only_from_research_records()
    {
        Assert.Equal(SiteEvidence.Unknown, ResearchReadiness.Evidence([], ScreeningDecision.NotReviewed));
        // Unknown-status rows are "not checked yet", not research.
        Assert.Equal(SiteEvidence.Unknown, ResearchReadiness.Evidence([F(FeasibilityItemKey.Zoning, FeasibilityStatus.Unknown)], ScreeningDecision.NotReviewed));
        Assert.Equal(SiteEvidence.Preliminary, ResearchReadiness.Evidence([], ScreeningDecision.Promising));
        Assert.Equal(SiteEvidence.Preliminary, ResearchReadiness.Evidence([F(FeasibilityItemKey.ElectricalService, FeasibilityStatus.Verified)], ScreeningDecision.NotReviewed));
        Assert.Equal(SiteEvidence.Verified, ResearchReadiness.Evidence([F(FeasibilityItemKey.ParcelIdentity, FeasibilityStatus.Verified)], ScreeningDecision.NotReviewed));
        // A parcel "verified" with no evidence text is not treated as verified.
        // Parcel recorded as Verified without evidence text: kept, but labelled Insufficient evidence — never Verified.
        Assert.Equal(SiteEvidence.InsufficientEvidence, ResearchReadiness.Evidence([F(FeasibilityItemKey.ParcelIdentity, FeasibilityStatus.Verified, null)], ScreeningDecision.NotReviewed));
        // A mismatch outranks a verified parcel.
        Assert.Equal(SiteEvidence.Mismatch, ResearchReadiness.Evidence(
            [F(FeasibilityItemKey.ParcelIdentity, FeasibilityStatus.Verified), F(FeasibilityItemKey.BusinessAtAddress, FeasibilityStatus.Concern)],
            ScreeningDecision.Promising));
        Assert.Equal(SiteEvidence.Mismatch, ResearchReadiness.Evidence([F(FeasibilityItemKey.ParcelIdentity, FeasibilityStatus.Concern)], ScreeningDecision.NotReviewed));
    }

    [Fact]
    public void Contact_readiness_needs_a_sourced_verified_decision_role()
    {
        Assert.Equal(ContactReadiness.NoContact, ResearchReadiness.Readiness([]));
        Assert.Equal(ContactReadiness.NoContact, ResearchReadiness.Readiness([C(ContactType.PropertyOwner, ContactVerificationStatus.Invalid)]));
        Assert.Equal(ContactReadiness.UnverifiedLead, ResearchReadiness.Readiness([C(ContactType.PropertyOwner, ContactVerificationStatus.Unverified)]));
        // Verified without a source is only a lead.
        Assert.Equal(ContactReadiness.UnverifiedLead, ResearchReadiness.Readiness([C(ContactType.PropertyOwner, ContactVerificationStatus.Verified, null)]));
        // A general company line / fleet rep is not a site decision-maker.
        Assert.Equal(ContactReadiness.GeneralBusinessContact, ResearchReadiness.Readiness([C(ContactType.Other, ContactVerificationStatus.Verified)]));
        Assert.Equal(ContactReadiness.GeneralBusinessContact, ResearchReadiness.Readiness([C(ContactType.FleetRepresentative, ContactVerificationStatus.Verified)]));
        foreach (var t in new[] { ContactType.PropertyOwner, ContactType.Broker, ContactType.FacilityManager, ContactType.SustainabilityManager, ContactType.EnergyEfficiencyManager })
            Assert.Equal(ContactReadiness.RelevantContactVerified, ResearchReadiness.Readiness([C(ContactType.Other, ContactVerificationStatus.Verified), C(t, ContactVerificationStatus.Verified)]));
    }

    [Fact]
    public void Best_contact_prefers_verified_relevant_roles()
    {
        var general = C(ContactType.Other, ContactVerificationStatus.Verified);
        var lead = C(ContactType.PropertyOwner, ContactVerificationStatus.Unverified);
        var owner = C(ContactType.PropertyOwner, ContactVerificationStatus.Verified);
        Assert.Same(owner, ResearchReadiness.BestContact([general, lead, owner]));
        Assert.Same(general, ResearchReadiness.BestContact([lead, general]));
        Assert.Null(ResearchReadiness.BestContact([C(ContactType.Broker, ContactVerificationStatus.Invalid)]));
    }

    [Fact]
    public void Best_contact_prefers_owner_then_broker_then_managers_among_verified()
    {
        var energy = C(ContactType.EnergyEfficiencyManager, ContactVerificationStatus.Verified);
        var broker = C(ContactType.Broker, ContactVerificationStatus.Verified);
        var owner = C(ContactType.PropertyOwner, ContactVerificationStatus.Verified);
        Assert.Same(owner, ResearchReadiness.BestContact([energy, broker, owner]));
        Assert.Same(broker, ResearchReadiness.BestContact([energy, broker]));
        var top = ResearchReadiness.TopContact([energy, broker, C(ContactType.Other, ContactVerificationStatus.Invalid)])!;
        Assert.Equal(ContactType.Broker, top.Role);
        Assert.Equal(1, top.OtherContacts); // invalid contacts are not counted
        Assert.Null(ResearchReadiness.TopContact([]));
        Assert.False(ResearchReadiness.TopContact([C(ContactType.PropertyOwner, ContactVerificationStatus.Verified, null)])!.Verified);
    }

    [Theory]
    [InlineData("verified,preliminary,mismatch,unknown,bogus", 4)]
    [InlineData("VERIFIED", 1)]
    public void Evidence_tokens_parse(string tokens, int expected) =>
        Assert.Equal(expected, ResearchReadiness.ParseEvidence(tokens.Split(',')).Length);

    [Fact]
    public void Evidence_links_are_extracted_from_free_text()
    {
        var links = SiteValidation.Links("Checked https://a.example/x, and (https://b.example/y). Not www.c.example").ToList();
        Assert.Equal(["https://a.example/x", "https://b.example/y"], links);
    }
}
