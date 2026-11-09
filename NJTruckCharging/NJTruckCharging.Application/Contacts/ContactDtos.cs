using System.ComponentModel.DataAnnotations;
using NJTruckCharging.Domain.Entities;

namespace NJTruckCharging.Application.Contacts;

public sealed record CreateContactRequest(
    [Required] string SiteId,
    [Required] string Name,
    string? Organization,
    string? JobTitle,
    ContactType ContactType,
    string? BusinessPhone,
    [EmailAddress] string? Email,
    [Url] string? SourceUrl,
    ContactVerificationStatus? VerificationStatus,
    string? ResearchNotes,
    [MaxLength(200)] string? RecordedBy = null);

public sealed record UpdateContactRequest(
    [Required] string Name,
    string? Organization,
    string? JobTitle,
    ContactType ContactType,
    string? BusinessPhone,
    [EmailAddress] string? Email,
    [Url] string? SourceUrl,
    ContactVerificationStatus VerificationStatus,
    string? ResearchNotes,
    [MaxLength(200)] string? RecordedBy = null,
    [MaxLength(2000)] string? CorrectionNote = null);

public sealed record SiteContactDto(
    int Id, string SiteId, string Name, string? Organization, string? JobTitle, ContactType ContactType,
    string? BusinessPhone, string? Email, string? SourceUrl,
    ContactVerificationStatus VerificationStatus, DateTimeOffset? LastVerifiedUtc, string? VerifiedBy,
    string? ResearchNotes, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, string? CreatedBy);

/// <summary>
/// Which of the four decision-maker states a site is in, for a clear at-a-glance distinction.
/// </summary>
public enum SiteContactState
{
    /// <summary>No researched contact and no nearby carrier.</summary>
    None,
    /// <summary>Only a nearby carrier is known (near the site, NOT confirmed to operate there).</summary>
    NearbyCarrierOnly,
    /// <summary>At least one researched contact exists but none is verified yet.</summary>
    PotentialUnverified,
    /// <summary>At least one verified decision-maker.</summary>
    VerifiedDecisionMaker
}

/// <summary>Per-site contact rollup used to drive the UI distinction and badges.</summary>
public sealed record SiteContactSummaryDto(
    string SiteId,
    int Total,
    int Verified,
    int Unverified,
    int Invalid,
    int NearbyCarriers,
    SiteContactState State);
