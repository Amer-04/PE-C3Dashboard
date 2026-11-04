using System.Linq.Expressions;
using NJTruckCharging.Domain.Entities;

namespace NJTruckCharging.Application.Sites;

/// <summary>
/// How far the team's own research backs up a site's identity. Derived ONLY from research records (feasibility
/// verdicts and screening decisions) — never from the C3 score or the source dataset.
/// </summary>
public enum SiteEvidence
{
    /// <summary>No research recorded for the site yet.</summary>
    Unknown = 0,
    /// <summary>Some research recorded (screening or feasibility checks), but the parcel is not verified yet.</summary>
    Preliminary = 1,
    /// <summary>A reviewer verified the official parcel identification, with evidence.</summary>
    Verified = 2,
    /// <summary>A reviewer recorded a parcel or business-at-address discrepancy.</summary>
    Mismatch = 3
}

/// <summary>
/// Whether a relevant professional contact is on record. Derived ONLY from researched contacts. A general company
/// line is not a site decision-maker, and nothing counts as verified without a cited source.
/// </summary>
public enum ContactReadiness
{
    NoContact = 0,
    /// <summary>A contact is recorded but not verified (or was marked verified without a source).</summary>
    UnverifiedLead = 1,
    /// <summary>A sourced, verified contact whose role is not a site decision-maker (general company line, fleet rep…).</summary>
    GeneralBusinessContact = 2,
    /// <summary>A sourced, verified owner, broker, facilities, sustainability or energy manager.</summary>
    RelevantContactVerified = 3
}

/// <summary>Most relevant researched contact for a site, as shown in the ranked table.
/// <paramref name="Verified"/> is true only for a Verified contact with a cited source.</summary>
public sealed record TopContactDto(
    string Name, string? JobTitle, string? Organization, ContactType Role,
    string? Email, string? Phone, bool Verified, string? SourceUrl, int OtherContacts);

/// <summary>
/// Single source of the Site Evidence and Contact Readiness rules: the in-memory form (list rows, detail, CSV) and the
/// SQL form (filters) below must stay equivalent — tests compare them.
/// </summary>
public static class ResearchReadiness
{
    public const FeasibilityItemKey ParcelKey = FeasibilityItemKey.ParcelIdentity;
    public const FeasibilityItemKey BusinessKey = FeasibilityItemKey.BusinessAtAddress;

    /// <summary>Roles that can speak for a site's charging decision (owner, broker, facilities / sustainability / energy).</summary>
    public static bool IsRelevantRole(ContactType t) =>
        t is ContactType.PropertyOwner or ContactType.Broker or ContactType.FacilityManager
            or ContactType.SustainabilityManager or ContactType.EnergyEfficiencyManager;

    public static bool IsSourced(SiteContact c) => !string.IsNullOrEmpty(c.SourceUrl);

    // ---------------- in memory ----------------

    public static SiteEvidence Evidence(IEnumerable<FeasibilityItem> siteItems, ScreeningDecision decision)
    {
        var reviewed = siteItems.Where(f => f.Status != FeasibilityStatus.Unknown).ToList();
        if (reviewed.Any(f => (f.ItemKey is ParcelKey or BusinessKey) && f.Status == FeasibilityStatus.Concern))
            return SiteEvidence.Mismatch;
        if (reviewed.Any(f => f.ItemKey == ParcelKey && f.Status == FeasibilityStatus.Verified && !string.IsNullOrEmpty(f.Evidence)))
            return SiteEvidence.Verified;
        return reviewed.Count > 0 || decision != ScreeningDecision.NotReviewed ? SiteEvidence.Preliminary : SiteEvidence.Unknown;
    }

    public static ContactReadiness Readiness(IEnumerable<SiteContact> siteContacts)
    {
        var list = siteContacts.ToList();
        bool VerifiedSourced(SiteContact c) => c.VerificationStatus == ContactVerificationStatus.Verified && IsSourced(c);
        if (list.Any(c => VerifiedSourced(c) && IsRelevantRole(c.ContactType))) return ContactReadiness.RelevantContactVerified;
        if (list.Any(VerifiedSourced)) return ContactReadiness.GeneralBusinessContact;
        return list.Any(c => c.VerificationStatus != ContactVerificationStatus.Invalid)
            ? ContactReadiness.UnverifiedLead
            : ContactReadiness.NoContact;
    }

    /// <summary>
    /// The contact to show first (table, Site Details and research CSV): verified relevant → verified general →
    /// unverified relevant → other lead. Within a group: owner, broker, facility, sustainability, energy manager, then
    /// others; most recently checked first.
    /// </summary>
    public static SiteContact? BestContact(IEnumerable<SiteContact> siteContacts) =>
        siteContacts.Where(c => c.VerificationStatus != ContactVerificationStatus.Invalid)
            .OrderBy(c => c.VerificationStatus == ContactVerificationStatus.Verified && IsSourced(c) ? 0 : 1)
            .ThenBy(c => IsRelevantRole(c.ContactType) ? 0 : 1)
            .ThenBy(c => RolePriority(c.ContactType))
            .ThenByDescending(c => c.LastVerifiedUtc ?? c.UpdatedAtUtc)
            .ThenBy(c => c.Id)
            .FirstOrDefault();

    /// <summary>Order in which decision-making roles are preferred.</summary>
    public static int RolePriority(ContactType t) => t switch
    {
        ContactType.PropertyOwner => 0, ContactType.Broker => 1, ContactType.FacilityManager => 2,
        ContactType.SustainabilityManager => 3, ContactType.EnergyEfficiencyManager => 4, _ => 5
    };

    /// <summary>The best contact as shown in the ranked table (null when none). Only recorded values; nothing inferred.</summary>
    public static TopContactDto? TopContact(IReadOnlyCollection<SiteContact> siteContacts)
    {
        var best = BestContact(siteContacts);
        if (best is null) return null;
        var others = siteContacts.Count(c => c != best && c.VerificationStatus != ContactVerificationStatus.Invalid);
        return new TopContactDto(best.Name, best.JobTitle, best.Organization, best.ContactType,
            best.Email, best.BusinessPhone,
            best.VerificationStatus == ContactVerificationStatus.Verified && IsSourced(best),
            best.SourceUrl, others);
    }

    // ---------------- tokens & labels ----------------

    /// <summary>Filter tokens: verified | preliminary | mismatch | unknown.</summary>
    public static SiteEvidence[] ParseEvidence(IEnumerable<string> tokens) =>
        tokens.Select(t => t.Trim().ToLowerInvariant() switch
            {
                "verified" => (SiteEvidence?)SiteEvidence.Verified,
                "preliminary" => SiteEvidence.Preliminary,
                "mismatch" => SiteEvidence.Mismatch,
                "unknown" => SiteEvidence.Unknown,
                _ => null
            }).Where(x => x is not null).Select(x => x!.Value).Distinct().ToArray();

    /// <summary>Filter tokens: relevant | general | lead | none.</summary>
    public static ContactReadiness[] ParseReadiness(IEnumerable<string> tokens) =>
        tokens.Select(t => t.Trim().ToLowerInvariant() switch
            {
                "relevant" => (ContactReadiness?)ContactReadiness.RelevantContactVerified,
                "general" => ContactReadiness.GeneralBusinessContact,
                "lead" => ContactReadiness.UnverifiedLead,
                "none" => ContactReadiness.NoContact,
                _ => null
            }).Where(x => x is not null).Select(x => x!.Value).Distinct().ToArray();

    public static string Label(SiteEvidence e) => e.ToString();

    public static string Label(ContactReadiness r) => r switch
    {
        ContactReadiness.RelevantContactVerified => "Relevant Contact Verified",
        ContactReadiness.GeneralBusinessContact => "General Business Contact",
        ContactReadiness.UnverifiedLead => "Unverified Lead",
        _ => "No Contact"
    };

    // ---------------- SQL (filters) ----------------

    public static Expression<Func<CandidateSite, bool>> EvidenceIn(
        IReadOnlyCollection<SiteEvidence> wanted, IQueryable<FeasibilityItem> feas, IQueryable<SiteScreening> screenings)
    {
        bool wM = wanted.Contains(SiteEvidence.Mismatch), wV = wanted.Contains(SiteEvidence.Verified),
             wP = wanted.Contains(SiteEvidence.Preliminary), wU = wanted.Contains(SiteEvidence.Unknown);
        const FeasibilityStatus unknown = FeasibilityStatus.Unknown, verified = FeasibilityStatus.Verified, concern = FeasibilityStatus.Concern;
        const ScreeningDecision notReviewed = ScreeningDecision.NotReviewed;
        return s =>
            (wM && feas.Any(f => f.SiteId == s.SiteId && (f.ItemKey == ParcelKey || f.ItemKey == BusinessKey) && f.Status == concern)) ||
            (wV && !feas.Any(f => f.SiteId == s.SiteId && (f.ItemKey == ParcelKey || f.ItemKey == BusinessKey) && f.Status == concern)
                && feas.Any(f => f.SiteId == s.SiteId && f.ItemKey == ParcelKey && f.Status == verified && f.Evidence != null && f.Evidence != "")) ||
            (wP && !feas.Any(f => f.SiteId == s.SiteId && (f.ItemKey == ParcelKey || f.ItemKey == BusinessKey) && f.Status == concern)
                && !feas.Any(f => f.SiteId == s.SiteId && f.ItemKey == ParcelKey && f.Status == verified && f.Evidence != null && f.Evidence != "")
                && (feas.Any(f => f.SiteId == s.SiteId && f.Status != unknown) || screenings.Any(x => x.SiteId == s.SiteId && x.Decision != notReviewed))) ||
            (wU && !feas.Any(f => f.SiteId == s.SiteId && f.Status != unknown) && !screenings.Any(x => x.SiteId == s.SiteId && x.Decision != notReviewed));
    }

    public static Expression<Func<CandidateSite, bool>> ReadinessIn(IReadOnlyCollection<ContactReadiness> wanted, IQueryable<SiteContact> contacts)
    {
        bool wR = wanted.Contains(ContactReadiness.RelevantContactVerified), wG = wanted.Contains(ContactReadiness.GeneralBusinessContact),
             wL = wanted.Contains(ContactReadiness.UnverifiedLead), wN = wanted.Contains(ContactReadiness.NoContact);
        const ContactVerificationStatus ver = ContactVerificationStatus.Verified, invalid = ContactVerificationStatus.Invalid;
        const ContactType owner = ContactType.PropertyOwner, broker = ContactType.Broker, facility = ContactType.FacilityManager,
            sustain = ContactType.SustainabilityManager, energy = ContactType.EnergyEfficiencyManager;
        return s =>
            (wR && contacts.Any(c => c.SiteId == s.SiteId && c.VerificationStatus == ver && c.SourceUrl != null && c.SourceUrl != ""
                && (c.ContactType == owner || c.ContactType == broker || c.ContactType == facility || c.ContactType == sustain || c.ContactType == energy))) ||
            (wG && !contacts.Any(c => c.SiteId == s.SiteId && c.VerificationStatus == ver && c.SourceUrl != null && c.SourceUrl != ""
                    && (c.ContactType == owner || c.ContactType == broker || c.ContactType == facility || c.ContactType == sustain || c.ContactType == energy))
                && contacts.Any(c => c.SiteId == s.SiteId && c.VerificationStatus == ver && c.SourceUrl != null && c.SourceUrl != "")) ||
            (wL && !contacts.Any(c => c.SiteId == s.SiteId && c.VerificationStatus == ver && c.SourceUrl != null && c.SourceUrl != "")
                && contacts.Any(c => c.SiteId == s.SiteId && c.VerificationStatus != invalid)) ||
            (wN && !contacts.Any(c => c.SiteId == s.SiteId && c.VerificationStatus != invalid));
    }
}
