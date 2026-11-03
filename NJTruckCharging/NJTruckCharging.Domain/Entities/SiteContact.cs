namespace NJTruckCharging.Domain.Entities;

/// <summary>
/// A researched person who can influence a C3 charging decision at a candidate site (owner, broker,
/// facility/sustainability/energy manager, fleet rep …). User-owned research data: like
/// <see cref="SavedSite"/> it references the stable <see cref="CandidateSite.SiteId"/> (soft reference,
/// no FK) so it survives workbook re-imports. A site may have many contacts.
///
/// This is distinct from <see cref="NearbyFleet"/>: a NearbyFleet is a carrier located *near* the site,
/// not a confirmed decision-maker *for* the property. Contact details are only ever entered by a
/// researcher from a cited source — never inferred from a nearby carrier.
/// </summary>
public class SiteContact
{
    public int Id { get; set; }

    /// <summary>Stable candidate key (<see cref="CandidateSite.SiteId"/>).</summary>
    public string SiteId { get; set; } = default!;

    public string Name { get; set; } = default!;
    public string? Organization { get; set; }
    public string? JobTitle { get; set; }
    public ContactType ContactType { get; set; } = ContactType.Other;

    public string? BusinessPhone { get; set; }
    public string? Email { get; set; }

    /// <summary>Where the contact detail came from (the evidence) — required discipline against invented data.</summary>
    public string? SourceUrl { get; set; }

    public ContactVerificationStatus VerificationStatus { get; set; } = ContactVerificationStatus.Unverified;
    public DateTimeOffset? LastVerifiedUtc { get; set; }
    public string? VerifiedBy { get; set; }

    public string? ResearchNotes { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public string? CreatedBy { get; set; }
}
