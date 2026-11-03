using System.Globalization;
using System.Text.RegularExpressions;
using NJTruckCharging.Domain.Entities;

namespace NJTruckCharging.Application.Sites;

/// <summary>State of one validation question. Nothing is Verified without a reviewer verdict or sourced contact.</summary>
public enum ValidationStatus
{
    /// <summary>Nothing on record.</summary>
    Unknown = 0,
    /// <summary>Only public-record/source data or an unverified lead — not checked by the team.</summary>
    Unconfirmed = 1,
    Verified = 2,
    /// <summary>A reviewer recorded a discrepancy or concern.</summary>
    Mismatch = 3
}

public sealed record ValidationFactDto(string Label, string Value);

/// <summary>One of the four separate questions: parcel, business at address, owner/representative, suitability.</summary>
public sealed record ValidationSectionDto(
    string Key, string Title, ValidationStatus Status, string StatusLabel,
    IReadOnlyList<ValidationFactDto> Facts,
    string? Finding,
    IReadOnlyList<string> EvidenceLinks,
    DateTimeOffset? CheckedAtUtc,
    IReadOnlyList<string> Discrepancies,
    string? NextStep);

public sealed record SiteValidationDto(
    SiteEvidence SiteEvidence, ContactReadiness ContactReadiness,
    IReadOnlyList<ValidationSectionDto> Sections,
    IReadOnlyList<string> MissingVerification);

/// <summary>
/// Keeps four things apart that are easy to conflate: the official parcel, the business operating at the address,
/// the owner / authorized representative, and actual truck-charging suitability. Source data is labelled as such;
/// only reviewer verdicts (with evidence) and sourced, verified contacts count as Verified.
/// </summary>
public static partial class SiteValidation
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    [GeneratedRegex(@"https?://[^\s<>""')\]]+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();

    /// <summary>http(s) links quoted in free-text evidence.</summary>
    public static IEnumerable<string> Links(string? text) =>
        string.IsNullOrWhiteSpace(text) ? [] : UrlRegex().Matches(text).Select(m => m.Value.TrimEnd('.', ',', ';'));

    private static readonly FeasibilityItemKey[] SuitabilityKeys =
    [
        FeasibilityItemKey.EligibleHighwayAccess, FeasibilityItemKey.UsableLand, FeasibilityItemKey.TruckParkingCapacity,
        FeasibilityItemKey.TruckEntranceAccess, FeasibilityItemKey.TurningManeuveringSpace, FeasibilityItemKey.ElectricalService,
        FeasibilityItemKey.FloodEnvironmental, FeasibilityItemKey.Zoning
    ];

    public static SiteValidationDto Build(
        CandidateSite s,
        IReadOnlyDictionary<FeasibilityItemKey, FeasibilityItem> feas,
        IReadOnlyList<SiteContact> contacts,
        ScreeningDecision decision,
        SiteSuitabilityDto suitability)
    {
        FeasibilityItem? F(FeasibilityItemKey k) => feas.TryGetValue(k, out var f) && f.Status != FeasibilityStatus.Unknown ? f : null;

        var sections = new List<ValidationSectionDto>
        {
            Parcel(s, F(ResearchReadiness.ParcelKey)),
            Business(s, F(ResearchReadiness.BusinessKey)),
            Owner(s, contacts, F(FeasibilityItemKey.OwnershipLeaseAvailability)),
            Suitability(SuitabilityKeys.Select(F).Where(f => f is not null).Select(f => f!).ToList(), suitability),
        };
        var missing = sections.Where(x => x.Status != ValidationStatus.Verified && x.NextStep is not null).Select(x => x.NextStep!)
            .Concat(suitability.RemainingToVerify)
            .Distinct().ToList();
        return new SiteValidationDto(
            ResearchReadiness.Evidence(feas.Values, decision), ResearchReadiness.Readiness(contacts), sections, missing);
    }

    private static string Label(ValidationStatus s, string mismatchWord = "Mismatch") => s switch
    {
        ValidationStatus.Verified => "Verified",
        ValidationStatus.Mismatch => mismatchWord,
        ValidationStatus.Unconfirmed => "Not verified",
        _ => "Unknown"
    };

    private static (ValidationStatus, string?, IReadOnlyList<string>, DateTimeOffset?) Review(FeasibilityItem? r) => r switch
    {
        null => (ValidationStatus.Unknown, null, [], null),
        { Status: FeasibilityStatus.Verified } => (ValidationStatus.Verified, $"Verified: {r.Evidence}", Links(r.Evidence).ToList(), r.ReviewedAtUtc ?? r.UpdatedAtUtc),
        _ => (ValidationStatus.Mismatch, $"Discrepancy: {r.Evidence}", Links(r.Evidence).ToList(), r.ReviewedAtUtc ?? r.UpdatedAtUtc)
    };

    private static string Or(string? v, string unknown = "Unknown") => string.IsNullOrWhiteSpace(v) ? unknown : v;

    // 1 — Official parcel identification (NJ tax-parcel record vs the team's check of the tax map).
    private static ValidationSectionDto Parcel(CandidateSite s, FeasibilityItem? review)
    {
        var (status, finding, links, at) = Review(review);
        if (status == ValidationStatus.Unknown) status = ValidationStatus.Unconfirmed; // a public record always exists
        var facts = new List<ValidationFactDto>
        {
            new("Tax-record address", $"{s.SiteAddress}, {s.Municipality} ({s.County} County)"),
            new("Block / lot", $"Block {Or(s.Block)}, Lot {Or(s.Lot)}{(string.IsNullOrWhiteSpace(s.Qual) ? "" : ", Qual " + s.Qual)}"),
            new("PAMS PIN", Or(s.PamsPin)),
            new("Acres (mapped / recorded)", $"{s.Acres.ToString("0.##", Inv)} / {(s.RecordedAcres > 0 ? s.RecordedAcres.ToString("0.##", Inv) : "Unknown")}"),
            new("Property class", s.PropClass),
        };
        if (s.LotsInSite > 1) facts.Add(new("Merged lots", $"{s.LotsInSite} adjacent same-owner lots"));
        var discrepancies = SiteSuitability.ParcelIssues(s);
        if (review is { Status: FeasibilityStatus.Concern }) discrepancies.Insert(0, $"Reviewer: {review.Evidence}");
        return new("Parcel", "Official parcel identification", status, Label(status), facts,
            finding ?? "Source: NJ tax-parcel records (MOD-IV / parcel GIS) — not yet checked against the county tax map.",
            links, at, discrepancies,
            "Confirm block/lot, address and boundary on the county tax map and record it under “Official parcel identification”");
    }

    // 2 — Business operating at the address (source listing vs the team's confirmation).
    private static ValidationSectionDto Business(CandidateSite s, FeasibilityItem? review)
    {
        var (status, finding, links, at) = Review(review);
        var listed = !string.IsNullOrWhiteSpace(s.BusinessesOnSite);
        if (status == ValidationStatus.Unknown && listed) status = ValidationStatus.Unconfirmed;
        var facts = new List<ValidationFactDto>
        {
            new("Listed in source data", listed ? s.BusinessesOnSite! : "None listed"),
        };
        if (!string.IsNullOrWhiteSpace(s.BusinessCategories)) facts.Add(new("Categories", s.BusinessCategories!));
        if (!string.IsNullOrWhiteSpace(s.BusinessPhone)) facts.Add(new("Listed phone", $"{s.BusinessPhone} (general business line — not a site decision-maker)"));
        if (!string.IsNullOrWhiteSpace(s.BusinessWebsite)) facts.Add(new("Listed website", s.BusinessWebsite!));
        var discrepancies = new List<string>();
        if (review is { Status: FeasibilityStatus.Concern }) discrepancies.Add($"Reviewer: {review.Evidence}");
        if (s.BusyMultiTenant) discrepancies.Add("6+ businesses listed — confirm which one actually operates on this parcel");
        return new("Business", "Business operating at the address", status, Label(status), facts,
            finding ?? (listed ? "Source: business listing matched to the parcel by location — not confirmed on site." : null),
            links, at, discrepancies,
            "Confirm which business operates at the address (Street View, signage, company website) and record it under “Business operating at the address”");
    }

    // 3 — Owner or authorized representative (who can sign for the property).
    private static ValidationSectionDto Owner(CandidateSite s, IReadOnlyList<SiteContact> contacts, FeasibilityItem? ownership)
    {
        static bool OwnerSide(SiteContact c) => c.ContactType is ContactType.PropertyOwner or ContactType.Broker;
        var ownerSide = contacts.Where(OwnerSide).ToList();
        var verified = ownerSide.Where(c => c.VerificationStatus == ContactVerificationStatus.Verified && ResearchReadiness.IsSourced(c)).ToList();
        var invalid = ownerSide.Where(c => c.VerificationStatus == ContactVerificationStatus.Invalid).ToList();
        var leads = ownerSide.Except(verified).Except(invalid).ToList();

        var facts = new List<ValidationFactDto>
        {
            new("Owner (tax record)", Or(s.OwnerName, "Not public (Daniel's Law)")),
            new("Owner mailing address", s.OwnerMailAddress is null ? "Unknown"
                : $"{s.OwnerMailAddress}{(s.OwnerMailCity is null ? "" : ", " + s.OwnerMailCity)} {s.OwnerMailZip}".Trim()
                  + (s.OwnerOccupiedHint ? " (likely owner-occupied)" : "")),
        };
        foreach (var c in verified.Concat(leads))
            facts.Add(new(c.VerificationStatus == ContactVerificationStatus.Verified ? "Verified contact" : "Unverified lead",
                string.Join(" · ", new[] { c.Name, c.JobTitle, c.Organization }.Where(x => !string.IsNullOrWhiteSpace(x)))));
        if (ownership is not null)
            facts.Add(new("Ownership / lease review", $"{ownership.Status}: {ownership.Evidence}"));

        var discrepancies = invalid.Select(c => $"{c.Name} was checked and is not a valid owner contact").ToList();
        if (ownership is { Status: FeasibilityStatus.Concern }) discrepancies.Add($"Ownership / lease: {ownership.Evidence}");

        var status = verified.Count > 0 ? ValidationStatus.Verified
            : leads.Count > 0 || s.OwnerMailAddress is not null ? ValidationStatus.Unconfirmed
            : invalid.Count > 0 ? ValidationStatus.Mismatch
            : ValidationStatus.Unknown;
        var links = verified.Concat(leads).Where(ResearchReadiness.IsSourced).Select(c => c.SourceUrl!)
            .Concat(Links(ownership?.Evidence)).Distinct().ToList();
        var checkedAt = verified.Concat(leads).Select(c => c.LastVerifiedUtc ?? c.UpdatedAtUtc)
            .Append(ownership?.ReviewedAtUtc ?? DateTimeOffset.MinValue).Max();
        return new("Owner", "Property owner or authorized representative", status, Label(status), facts,
            verified.Count > 0 ? "Verified from a cited source." : "No verified owner or broker yet. A general company phone is not a site decision-maker.",
            links, checkedAt == DateTimeOffset.MinValue ? null : checkedAt, discrepancies,
            "Identify the property owner or listing broker from a published source and add them as a contact");
    }

    // 4 — Actual suitability for truck charging (summary of the reviewer's checks; details in "At a glance").
    private static ValidationSectionDto Suitability(IReadOnlyList<FeasibilityItem> reviewed, SiteSuitabilityDto suit)
    {
        var concerns = reviewed.Where(r => r.Status == FeasibilityStatus.Concern).ToList();
        var siteItems = suit.Items.Where(i => i.Key is not ("Identity" or "Contact")).ToList();
        var allVerifiedYes = siteItems.All(i => i.Basis == SuitabilityBasis.Verified && i.Answer == SuitabilityAnswer.Yes);
        var status = concerns.Count > 0 ? ValidationStatus.Mismatch
            : allVerifiedYes ? ValidationStatus.Verified
            : reviewed.Count > 0 || siteItems.Any(i => i.Basis is SuitabilityBasis.Estimate or SuitabilityBasis.SourceData) ? ValidationStatus.Unconfirmed
            : ValidationStatus.Unknown;
        // Compact: one line per check (the full answers are listed in "At a glance" right below).
        static string Short(string key) => key switch
        {
            "Highway" => "Highway access", "Land" => "Usable land", "Capacity" => "Parking (20–30+)",
            "TruckSpaces" => "Truck-suitable spaces", "Access" => "Truck entrance & turning", "Power" => "Electrical capacity", _ => key
        };
        static string Basis(SuitabilityBasis b) => b switch
        {
            SuitabilityBasis.Verified => "verified", SuitabilityBasis.Estimate => "estimate", SuitabilityBasis.SourceData => "public records",
            SuitabilityBasis.Unverified => "unverified", _ => "no data"
        };
        var facts = siteItems.Select(i => new ValidationFactDto(Short(i.Key),
            $"{(i.Answer == SuitabilityAnswer.NeedsCheck ? "Check" : i.Answer.ToString())} · {Basis(i.Basis)}")).ToList();
        var verifiedCount = siteItems.Count(i => i.Basis == SuitabilityBasis.Verified);
        return new("Suitability", "Actual suitability for truck charging", status, Label(status, "Concern"), facts,
            $"{verifiedCount} of {siteItems.Count} site checks verified by a reviewer; the rest are estimates or public records.",
            reviewed.SelectMany(r => Links(r.Evidence)).Distinct().ToList(),
            reviewed.Count == 0 ? null : reviewed.Max(r => r.ReviewedAtUtc ?? r.UpdatedAtUtc),
            concerns.Select(c => $"{Feasibility.FeasibilityLabels.For(c.ItemKey)}: {c.Evidence}").ToList(),
            allVerifiedYes ? null : "Complete the feasibility review (access, parking count, power) with evidence");
    }
}
