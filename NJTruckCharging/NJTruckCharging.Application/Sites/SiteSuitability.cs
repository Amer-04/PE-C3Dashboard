using System.Globalization;
using System.Text.RegularExpressions;
using NJTruckCharging.Domain.Entities;

namespace NJTruckCharging.Application.Sites;

/// <summary>Plain-language answer to one suitability question.</summary>
public enum SuitabilityAnswer
{
    /// <summary>No information yet — never treated as a pass.</summary>
    Unknown = 0,
    Yes = 1,
    /// <summary>Probably fine or partly met, but something must be checked.</summary>
    NeedsCheck = 2,
    No = 3
}

/// <summary>Where the answer comes from — so estimates are never mistaken for verified facts.</summary>
public enum SuitabilityBasis
{
    /// <summary>Nothing on record.</summary>
    Missing = 0,
    /// <summary>Public records / GIS in the C3 dataset (not field-checked).</summary>
    SourceData = 1,
    /// <summary>A desktop estimate derived from source data.</summary>
    Estimate = 2,
    /// <summary>A reviewer recorded a verdict with evidence (feasibility review or verified contact).</summary>
    Verified = 3,
    /// <summary>Entered by the team but not yet verified (e.g. an unverified contact lead).</summary>
    Unverified = 4
}

public sealed record SuitabilityItemDto(
    string Key, string Question, SuitabilityAnswer Answer, SuitabilityBasis Basis,
    string Headline, string Detail, string? NextStep);

/// <summary>"At a glance" summary for the Site Details panel. It informs; it never decides (no score, no winner).</summary>
public sealed record SiteSuitabilityDto(
    IReadOnlyList<SuitabilityItemDto> Items,
    IReadOnlyList<string> RemainingToVerify,
    int Yes, int NeedsCheck, int No, int Unknown, int VerifiedAnswers)
{
    /// <summary>
    /// Preliminary business preferences (parking, highway proximity, truck ingress, battery-storage room). Kept
    /// separate from the C3 score and from the eligibility answers above — they never change scoring or ranking.
    /// </summary>
    public IReadOnlyList<SuitabilityItemDto> Preferences { get; init; } = [];
    public int PreferencesMet => Preferences.Count(p => p.Answer == SuitabilityAnswer.Yes);
}

/// <summary>
/// Answers the business owner's questions for one site from the C3 dataset plus the team's own research
/// (feasibility verdicts, contacts). Rules (thresholds are business targets, not C3 scoring inputs):
/// <list type="bullet">
/// <item>Parking target: at least <see cref="MinSpaces"/> spaces, <see cref="PreferredSpaces"/>+ preferred.</item>
/// <item>Usable land: open acres × <see cref="SiteFilter.EstimatedTrailersPerOpenAcre"/> trailer stalls (preliminary).</item>
/// <item>Only a reviewer-verified count is treated as medium/heavy-duty-suitable spaces.</item>
/// <item>Unknown stays Unknown; estimates are labelled Estimate; nothing is "Verified" without a reviewer verdict.</item>
/// </list>
/// </summary>
public static partial class SiteSuitability
{
    public const int MinSpaces = 20;
    public const int PreferredSpaces = 30;
    /// <summary>Methodology: distances are straight-line; check driving distance beyond ~4 miles.</summary>
    public const double CheckDrivingBeyondMiles = 4.0;
    public const double CloseToExitMiles = 1.0;
    /// <summary>Mapped parcel area differing from the deed (recorded) acreage by more than this share is flagged.</summary>
    public const double AcreageMismatchShare = 0.5;
    /// <summary>A 53-ft tractor-trailer needs room to turn; parcels narrower than this are flagged for checking.</summary>
    public const double NarrowParcelFeet = 150;

    // ---- Preliminary business preferences (business assumptions, NOT C3 scoring rules) ----
    /// <summary>Preferred straight-line distance to the eligible exit.</summary>
    public const double PreferredExitMiles = 1.0;
    /// <summary>
    /// Rough open-land allowance for battery storage: one or two containerized BESS units, transformer/switchgear and
    /// fire-code clearances (~0.25 ac). A desktop assumption to flag room, not an engineering size.
    /// </summary>
    public const double BatteryStorageAllowanceAcres = 0.25;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    [GeneratedRegex(@"(\d[\d,]*)\s*\+?\s+(?:[a-z\-/]+\s+){0,3}?(stalls?|spaces?|spots?|bays?|positions?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex CountRegex();

    [GeneratedRegex(@"^\s*\d")]
    private static partial Regex StartsWithNumber();

    /// <summary>Reads a space count such as "~48 marked trailer stalls" from reviewer evidence; null if none stated.</summary>
    public static int? ParseSpaceCount(string? evidence)
    {
        if (string.IsNullOrWhiteSpace(evidence)) return null;
        var m = CountRegex().Match(evidence);
        return m.Success && int.TryParse(m.Groups[1].Value.Replace(",", ""), NumberStyles.Integer, Inv, out var n) ? n : null;
    }

    public static SiteSuitabilityDto Build(
        CandidateSite s,
        IReadOnlyDictionary<FeasibilityItemKey, FeasibilityItem> feas,
        IReadOnlyList<SiteContact> contacts)
    {
        FeasibilityItem? F(FeasibilityItemKey k) => feas.TryGetValue(k, out var f) && f.Status != FeasibilityStatus.Unknown ? f : null;
        static string Ev(FeasibilityItem f) => string.IsNullOrWhiteSpace(f.Evidence) ? "no evidence text recorded" : f.Evidence!;

        var items = new List<SuitabilityItemDto>
        {
            Identity(s, F(FeasibilityItemKey.ParcelIdentity), Ev),
            Highway(s, F(FeasibilityItemKey.EligibleHighwayAccess), Ev),
            Land(s, F(FeasibilityItemKey.UsableLand), Ev),
            Capacity(s, F(FeasibilityItemKey.TruckParkingCapacity), Ev),
            TruckSpaces(s, F(FeasibilityItemKey.TruckParkingCapacity), Ev),
            Access(s, F(FeasibilityItemKey.TruckEntranceAccess), F(FeasibilityItemKey.TurningManeuveringSpace), Ev),
            Power(s, F(FeasibilityItemKey.ElectricalService), Ev),
            Contact(s, contacts),
        };

        var remaining = items
            .Where(i => i.NextStep is not null && (i.Answer != SuitabilityAnswer.Yes || i.Basis != SuitabilityBasis.Verified))
            .Select(i => i.NextStep!)
            .Distinct()
            .ToList();

        return new SiteSuitabilityDto(items, remaining,
            items.Count(i => i.Answer == SuitabilityAnswer.Yes), items.Count(i => i.Answer == SuitabilityAnswer.NeedsCheck),
            items.Count(i => i.Answer == SuitabilityAnswer.No), items.Count(i => i.Answer == SuitabilityAnswer.Unknown),
            items.Count(i => i.Basis == SuitabilityBasis.Verified))
        {
            Preferences = Preferences(s, items)
        };
    }

    /// <summary>
    /// Preliminary business preferences, derived from the answers above plus one battery-storage room check.
    /// Never verified on their own and never part of the C3 score.
    /// </summary>
    private static List<SuitabilityItemDto> Preferences(CandidateSite s, IReadOnlyList<SuitabilityItemDto> answers)
    {
        SuitabilityItemDto A(string key) => answers.Single(a => a.Key == key);
        var cap = A(nameof(Capacity));
        var access = A(nameof(Access));

        // Parking: 30+ preferred, 20 minimum — same rating as the capacity answer, labelled as a preference.
        var parking = cap with
        {
            Key = "PrefParking", Question = $"Preference: {PreferredSpaces}+ parking spaces (minimum {MinSpaces})",
            NextStep = null
        };

        // Highway: within ~1 mi of the eligible exit preferred.
        var miles = s.MilesToExit;
        var hwyText = $"{miles.ToString("0.0#", Inv)} mi to {s.NearestHighway ?? "the exit"}{(string.IsNullOrEmpty(s.NearestExit) ? "" : " exit " + s.NearestExit)}";
        var highway = s.NearestHighway is null
            ? new SuitabilityItemDto("PrefHighway", $"Preference: within ~{PreferredExitMiles:0} mi of an eligible exit", SuitabilityAnswer.Unknown, SuitabilityBasis.Missing, "Unknown", "", null)
            : new SuitabilityItemDto("PrefHighway", $"Preference: within ~{PreferredExitMiles:0} mi of an eligible exit",
                miles <= PreferredExitMiles ? SuitabilityAnswer.Yes : SuitabilityAnswer.NeedsCheck,
                A(nameof(Highway)).Basis == SuitabilityBasis.Verified ? SuitabilityBasis.Verified : SuitabilityBasis.SourceData,
                miles <= PreferredExitMiles ? $"Yes — {hwyText}" : $"Farther than preferred — {hwyText}", "Straight-line distance.", null);

        // Truck ingress: a verified, usable truck entrance.
        var ingress = access.Answer switch
        {
            SuitabilityAnswer.Yes => access with { Key = "PrefIngress", Question = "Preference: verified truck ingress", NextStep = null },
            SuitabilityAnswer.No => access with { Key = "PrefIngress", Question = "Preference: verified truck ingress", NextStep = null },
            SuitabilityAnswer.NeedsCheck => access with { Key = "PrefIngress", Question = "Preference: verified truck ingress", Headline = "Entrance verified — maneuvering not yet", NextStep = null },
            _ => access with { Key = "PrefIngress", Question = "Preference: verified truck ingress", NextStep = null }
        };

        // Battery storage: is there open land left after the preferred parking? (desktop estimate only)
        const string bq = "Preference: room for battery storage";
        var parkingAcres = PreferredSpaces / SiteFilter.EstimatedTrailersPerOpenAcre;
        var needed = parkingAcres + BatteryStorageAllowanceAcres;
        var note = $"Assumes ~{BatteryStorageAllowanceAcres.ToString("0.##", Inv)} ac for containerized storage, transformer and clearances "
                 + $"beyond ~{parkingAcres.ToString("0.#", Inv)} ac for {PreferredSpaces} truck stalls. Open land may include parking in use, wetlands or slopes; "
                 + "siting needs the utility and fire-code review.";
        SuitabilityItemDto battery = s.OpenAcresEst <= 0
            ? new("PrefBattery", bq, SuitabilityAnswer.Unknown, SuitabilityBasis.Missing, "Unknown — no open-land estimate", note, null)
            : s.OpenAcresEst >= needed
                ? new("PrefBattery", bq, SuitabilityAnswer.Yes, SuitabilityBasis.Estimate,
                    $"Possible — ~{(s.OpenAcresEst - parkingAcres).ToString("0.#", Inv)} open ac beyond the parking (estimate)", note, null)
                : s.OpenAcresEst >= MinSpaces / SiteFilter.EstimatedTrailersPerOpenAcre
                    ? new("PrefBattery", bq, SuitabilityAnswer.NeedsCheck, SuitabilityBasis.Estimate,
                        "Tight — little open land left after parking (estimate)", note, null)
                    : new("PrefBattery", bq, SuitabilityAnswer.No, SuitabilityBasis.Estimate,
                        "Unlikely — not enough open land for parking plus storage (estimate)", note, null);

        return [parking, highway, ingress, battery];
    }

    // 1 — Is this a real, identifiable property?
    private static SuitabilityItemDto Identity(CandidateSite s, FeasibilityItem? review, Func<FeasibilityItem, string> ev)
    {
        const string q = "Is this a real, identifiable property?";
        var issues = ParcelIssues(s);
        var record = $"Tax parcel: Block {s.Block ?? "?"}, Lot {Short(s.Lot)}, {s.Municipality} ({s.County} County)";
        if (s.LotsInSite > 1) record += $"; {s.LotsInSite} adjacent lots with the same owner merged into one site";
        if (review is { Status: FeasibilityStatus.Verified })
            return new(nameof(Identity), q, SuitabilityAnswer.Yes, SuitabilityBasis.Verified,
                $"Yes — parcel verified: {s.SiteAddress}, {s.Municipality}", $"{record}. Verified: {ev(review)}", null);
        if (review is { Status: FeasibilityStatus.Concern })
            return new(nameof(Identity), q, SuitabilityAnswer.No, SuitabilityBasis.Verified,
                "Parcel mismatch recorded", $"{record}. Reviewer: {ev(review)}", "Resolve the parcel mismatch before shortlisting");

        return issues.Count == 0
            ? new(nameof(Identity), q, SuitabilityAnswer.Yes, SuitabilityBasis.SourceData,
                $"Yes — {s.SiteAddress}, {s.Municipality}", record + ". From NJ tax-parcel records (not field-checked).",
                null)
            : new(nameof(Identity), q, SuitabilityAnswer.NeedsCheck, SuitabilityBasis.SourceData,
                "Possible parcel mismatch — confirm the boundary", $"{record}. Check: {string.Join("; ", issues)}.",
                "Confirm the parcel boundary and address on the county tax map / satellite imagery");
    }

    /// <summary>Signals in the tax record that the mapped parcel may not be the property it appears to be.</summary>
    public static List<string> ParcelIssues(CandidateSite s)
    {
        var issues = new List<string>();
        if (!StartsWithNumber().IsMatch(s.SiteAddress))
            issues.Add($"the tax record has no street number (\"{s.SiteAddress}\")");
        if (s.RecordedAcres <= 0)
            issues.Add("no recorded (deed) acreage to cross-check the mapped parcel");
        else if (Math.Abs(s.Acres - s.RecordedAcres) / s.RecordedAcres > AcreageMismatchShare)
            issues.Add($"mapped parcel area ({s.Acres.ToString("0.#", Inv)} ac) differs from the recorded acreage ({s.RecordedAcres.ToString("0.#", Inv)} ac)");
        if (s.BuildingDescription?.Contains("COMMON ELEM", StringComparison.OrdinalIgnoreCase) == true)
            issues.Add("recorded as condominium \"common elements\" — shared land, not a single owner's property");
        else if (s.LandValue + s.ImprovementValue <= 0)
            issues.Add("assessed at $0 — often exempt, public, rail or condominium land; ownership may not be a single private owner");
        return issues;
    }

    // 2 — Is it near an eligible C3 highway?
    private static SuitabilityItemDto Highway(CandidateSite s, FeasibilityItem? review, Func<FeasibilityItem, string> ev)
    {
        const string q = "Is it near an eligible C3 highway?";
        var where = s.NearestHighway is null ? "an eligible highway"
            : $"{s.NearestHighway}{(string.IsNullOrEmpty(s.NearestExit) ? "" : " exit " + s.NearestExit)}";
        var dist = $"{s.MilesToExit.ToString("0.0#", Inv)} mi (straight line) to {where}";
        if (review is { Status: FeasibilityStatus.Verified })
            return new(nameof(Highway), q, SuitabilityAnswer.Yes, SuitabilityBasis.Verified, $"Yes — {dist}", $"Verified: {ev(review)}", null);
        if (review is { Status: FeasibilityStatus.Concern })
            return new(nameof(Highway), q, SuitabilityAnswer.No, SuitabilityBasis.Verified, $"Concern — {dist}", $"Reviewer concern: {ev(review)}",
                "Resolve the highway-access concern");
        if (s.NearestHighway is null)
            return new(nameof(Highway), q, SuitabilityAnswer.Unknown, SuitabilityBasis.Missing, "Unknown — no eligible exit on record", "",
                "Identify the nearest eligible interchange");
        return s.MilesToExit > CheckDrivingBeyondMiles
            ? new(nameof(Highway), q, SuitabilityAnswer.NeedsCheck, SuitabilityBasis.SourceData, $"Within the 5-mi screen — {dist}",
                "Straight-line distance is over ~4 mi; the driving route may exceed the eligible distance.", "Check the driving distance to the exit")
            : new(nameof(Highway), q, SuitabilityAnswer.Yes, SuitabilityBasis.SourceData,
                s.MilesToExit <= CloseToExitMiles ? $"Yes — close: {dist}" : $"Yes — {dist}",
                "All candidates are within 5 straight-line miles of an eligible interchange (I-95/NJ Turnpike, I-295 exit 47+, I-287, I-80, I-280, I-78).",
                "Confirm the truck route from the exit to the site entrance");
    }

    // 3 — Does it have sufficient usable land?
    private static SuitabilityItemDto Land(CandidateSite s, FeasibilityItem? review, Func<FeasibilityItem, string> ev)
    {
        const string q = "Does it have sufficient usable land?";
        var needAcres = MinSpaces / SiteFilter.EstimatedTrailersPerOpenAcre;
        var wantAcres = PreferredSpaces / SiteFilter.EstimatedTrailersPerOpenAcre;
        if (review is { Status: FeasibilityStatus.Verified })
            return new(nameof(Land), q, SuitabilityAnswer.Yes, SuitabilityBasis.Verified, "Yes — verified", ev(review), null);
        if (review is { Status: FeasibilityStatus.Concern })
            return new(nameof(Land), q, SuitabilityAnswer.No, SuitabilityBasis.Verified, "Concern — usable land", ev(review), "Resolve the usable-land concern");
        const string caveat = "Estimate = parcel area minus building footprints; it still includes parking in use, wetlands and slopes.";
        const string next = "Measure the actually usable open area on satellite imagery";
        var open = s.OpenAcresEst;
        if (open <= 0)
            return new(nameof(Land), q, SuitabilityAnswer.Unknown, SuitabilityBasis.Missing, "Unknown — no open-land estimate",
                $"Total parcel {s.Acres.ToString("0.##", Inv)} ac, but no building-footprint estimate is available.", next);
        var txt = $"~{open.ToString("0.#", Inv)} open ac of {s.Acres.ToString("0.#", Inv)} ac (estimate)";
        return open >= wantAcres
            ? new(nameof(Land), q, SuitabilityAnswer.Yes, SuitabilityBasis.Estimate, $"Likely — {txt}", caveat, next)
            : open >= needAcres
                ? new(nameof(Land), q, SuitabilityAnswer.NeedsCheck, SuitabilityBasis.Estimate, $"Borderline — {txt}",
                    $"About {needAcres.ToString("0.0", Inv)}–{wantAcres.ToString("0.0", Inv)} open acres are needed for {MinSpaces}–{PreferredSpaces} truck stalls. {caveat}", next)
                : new(nameof(Land), q, SuitabilityAnswer.No, SuitabilityBasis.Estimate, $"Likely too small — {txt}",
                    $"Under ~{needAcres.ToString("0.0", Inv)} open acres ({MinSpaces} truck stalls). {caveat}", next);
    }

    // 4 — Can it accommodate ~20–30+ spaces?
    private static SuitabilityItemDto Capacity(CandidateSite s, FeasibilityItem? review, Func<FeasibilityItem, string> ev)
    {
        const string q = "Can it fit about 20–30 or more parking spaces?";
        if (review is not null)
        {
            var n = ParseSpaceCount(review.Evidence);
            if (review.Status == FeasibilityStatus.Concern)
                return new(nameof(Capacity), q, SuitabilityAnswer.No, SuitabilityBasis.Verified, "Concern — parking capacity", ev(review), "Resolve the parking-capacity concern");
            if (n is null)
                return new(nameof(Capacity), q, SuitabilityAnswer.NeedsCheck, SuitabilityBasis.Verified, "Verified, but no count recorded", ev(review),
                    "Record the number of spaces in the parking-capacity review");
            return Rate(nameof(Capacity), q, n.Value, SuitabilityBasis.Verified, $"{n} spaces (verified)", ev(review), null);
        }
        var est = s.OpenAcresEst > 0 ? (int)(s.OpenAcresEst * SiteFilter.EstimatedTrailersPerOpenAcre) : (int?)null;
        return est is null
            ? new(nameof(Capacity), q, SuitabilityAnswer.Unknown, SuitabilityBasis.Missing, "Unknown — no open-land estimate", "",
                "Count the possible parking spaces on satellite imagery")
            : Rate(nameof(Capacity), q, est.Value, SuitabilityBasis.Estimate, $"~{est} spaces (preliminary estimate)",
                $"Desktop screen at ~{SiteFilter.EstimatedTrailersPerOpenAcre:0} trailer stalls per open acre — not a verified or buildable count.",
                "Count the possible parking spaces on satellite imagery");
    }

    private static SuitabilityItemDto Rate(string key, string q, int n, SuitabilityBasis basis, string what, string detail, string? next) =>
        n >= PreferredSpaces
            ? new(key, q, SuitabilityAnswer.Yes, basis, $"Yes — {what}", detail, next)
            : n >= MinSpaces
                ? new(key, q, SuitabilityAnswer.NeedsCheck, basis, $"Meets the minimum — {what}", $"Below the preferred {PreferredSpaces}. {detail}", next)
                : new(key, q, SuitabilityAnswer.No, basis, $"Below {MinSpaces} — {what}", detail, next);

    // 5 — How many spaces suit medium/heavy-duty trucks?
    private static SuitabilityItemDto TruckSpaces(CandidateSite s, FeasibilityItem? review, Func<FeasibilityItem, string> ev)
    {
        const string q = "How many spaces suit medium- and heavy-duty trucks?";
        const string next = "Count truck-suitable stalls (not car spaces) and record the number in the parking-capacity review";
        if (review is { Status: FeasibilityStatus.Verified } && ParseSpaceCount(review.Evidence) is { } n)
            return Rate(nameof(TruckSpaces), q, n, SuitabilityBasis.Verified, $"{n} truck-suitable spaces (verified)", ev(review), null);
        if (review is { Status: FeasibilityStatus.Concern })
            return new(nameof(TruckSpaces), q, SuitabilityAnswer.No, SuitabilityBasis.Verified, "Concern — truck parking", ev(review), next);
        var est = s.OpenAcresEst > 0 ? (int)(s.OpenAcresEst * SiteFilter.EstimatedTrailersPerOpenAcre) : (int?)null;
        return new(nameof(TruckSpaces), q, SuitabilityAnswer.Unknown, SuitabilityBasis.Missing, "Unknown — not counted yet",
            (est is null ? "No estimate is available. " : $"The ~{est}-space estimate assumes open land could take tractor-trailers; it is not a truck count. ")
            + "Existing striped car parking is not counted as truck-suitable.", next);
    }

    // 6 — Is the truck entrance accessible and suitable for maneuvering?
    private static SuitabilityItemDto Access(CandidateSite s, FeasibilityItem? entrance, FeasibilityItem? turning, Func<FeasibilityItem, string> ev)
    {
        const string q = "Is the truck entrance accessible, with room to maneuver?";
        var shape = s.ShortSideFt > 0
            ? (s.ShortSideFt < NarrowParcelFeet
                ? $"Parcel is narrow (shortest side ~{s.ShortSideFt.ToString("0", Inv)} ft) — a 53-ft tractor-trailer may not be able to turn."
                : $"Parcel shape: shortest side ~{s.ShortSideFt.ToString("0", Inv)} ft.")
            : "";
        var parts = new List<string>();
        if (entrance is not null) parts.Add($"Entrance — {entrance.Status}: {ev(entrance)}");
        if (turning is not null) parts.Add($"Turning space — {turning.Status}: {ev(turning)}");
        var detail = string.Join(" ", parts.Append(shape).Where(p => p.Length > 0));

        if (entrance?.Status == FeasibilityStatus.Concern || turning?.Status == FeasibilityStatus.Concern)
            return new(nameof(Access), q, SuitabilityAnswer.No, SuitabilityBasis.Verified, "Concern — truck access", detail, "Resolve the truck-access concern");
        if (entrance?.Status == FeasibilityStatus.Verified && turning?.Status == FeasibilityStatus.Verified)
            return new(nameof(Access), q, SuitabilityAnswer.Yes, SuitabilityBasis.Verified, "Yes — entrance and turning space verified", detail, null);
        if (entrance?.Status == FeasibilityStatus.Verified)
            return new(nameof(Access), q, SuitabilityAnswer.NeedsCheck, SuitabilityBasis.Verified, "Entrance verified — turning space not checked", detail,
                "Check tractor-trailer turning and maneuvering space on site");
        return new(nameof(Access), q, SuitabilityAnswer.Unknown, SuitabilityBasis.Missing, "Unknown — not reviewed", detail,
            "Check the truck entrance and turning space (Street View / site visit)");
    }

    // 7 — Is sufficient electrical capacity known or verified?
    private static SuitabilityItemDto Power(CandidateSite s, FeasibilityItem? review, Func<FeasibilityItem, string> ev)
    {
        const string q = "Is sufficient electrical capacity known or verified?";
        var utility = s.Utility is null ? "Electric utility unknown." : $"Utility territory: {s.Utility} (territory only — says nothing about available capacity).";
        if (review is { Status: FeasibilityStatus.Verified })
            return new(nameof(Power), q, SuitabilityAnswer.Yes, SuitabilityBasis.Verified, "Yes — verified", $"{ev(review)} {utility}", null);
        if (review is { Status: FeasibilityStatus.Concern })
            return new(nameof(Power), q, SuitabilityAnswer.No, SuitabilityBasis.Verified, "Concern — electrical capacity", $"{ev(review)} {utility}",
                "Ask the utility for a capacity / interconnection check");
        return new(nameof(Power), q, SuitabilityAnswer.Unknown, SuitabilityBasis.Missing, "Unknown — no capacity information", utility,
            "Ask the utility for a capacity / interconnection check");
    }

    // 8 — Is there a relevant person to contact?
    private static SuitabilityItemDto Contact(CandidateSite s, IReadOnlyList<SiteContact> contacts)
    {
        const string q = "Is there a relevant person to contact?";
        static string Role(ContactType t) => t switch
        {
            ContactType.PropertyOwner => "property owner", ContactType.Broker => "broker", ContactType.FacilityManager => "facility manager",
            ContactType.SustainabilityManager => "sustainability manager", ContactType.EnergyEfficiencyManager => "energy manager",
            ContactType.FleetRepresentative => "fleet representative", _ => "contact"
        };
        // Same rule as the Contact Readiness column: only a sourced, verified decision-making role is a "Yes".
        var verified = contacts.Where(c => c.VerificationStatus == ContactVerificationStatus.Verified && ResearchReadiness.IsSourced(c)).ToList();
        var relevant = verified.Where(c => ResearchReadiness.IsRelevantRole(c.ContactType)).ToList();
        var leads = contacts.Where(c => c.VerificationStatus != ContactVerificationStatus.Invalid).Except(verified).ToList();
        var fallback = $"Owner names are not public in NJ (Daniel's Law); owner mailing address: {s.OwnerMailAddress ?? "unknown"}{(s.OwnerMailCity is null ? "" : ", " + s.OwnerMailCity)}."
                       + (string.IsNullOrWhiteSpace(s.BusinessesOnSite) ? "" : $" Business on site: {s.BusinessesOnSite}.");
        static string Names(IEnumerable<SiteContact> cs) => string.Join(", ", cs.Select(c => $"{c.Name} ({Role(c.ContactType)})"));
        if (relevant.Count > 0)
            return new(nameof(Contact), q, SuitabilityAnswer.Yes, SuitabilityBasis.Verified, $"Yes — {Names(relevant)}",
                leads.Count > 0 ? $"Also {leads.Count} unverified lead(s)." : "Verified by the team from a cited source.", null);
        if (verified.Count > 0)
            return new(nameof(Contact), q, SuitabilityAnswer.NeedsCheck, SuitabilityBasis.Verified,
                $"General business contact only — {Names(verified)}",
                "A general company line is not a verified site decision-maker. " + fallback,
                "Find the property owner, broker or facilities / sustainability / energy manager");
        if (leads.Count > 0)
            return new(nameof(Contact), q, SuitabilityAnswer.NeedsCheck, SuitabilityBasis.Unverified,
                $"Possible — {Names(leads)}, not verified", fallback, "Verify the contact's role and details");
        return new(nameof(Contact), q, SuitabilityAnswer.Unknown, SuitabilityBasis.Missing, "No contact on file", fallback,
            "Research the owner or decision-maker (tax assessor / OPRA request, business on site)");
    }

    private static string Short(string? v) => v is null ? "?" : v.Length > 30 ? v[..30] + "…" : v;
}
