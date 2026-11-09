using System.Linq.Expressions;
using NJTruckCharging.Application.Sites;
using NJTruckCharging.Domain.Entities;

namespace NJTruckCharging.Application.Research;

/// <summary>The eight core verification checks that make up research completeness.</summary>
public enum ResearchCheck
{
    ParcelIdentity = 0,
    BusinessAtAddress = 1,
    TruckEntrance = 2,
    TurningSpace = 3,
    ParkingCapacity = 4,
    ElectricalService = 5,
    Zoning = 6,
    VerifiedContact = 7
}

/// <summary>State of one core check.</summary>
public enum CheckState
{
    /// <summary>Nothing recorded (or reset to Unknown).</summary>
    NotStarted = 0,
    /// <summary>A verdict exists but lacks evidence text, a source URL or an evidence date (incl. records made
    /// before Phase 1). Kept as-is and flagged — never deleted and never counted as reviewed or verified.</summary>
    InsufficientEvidence = 1,
    /// <summary>Sources disagree. Not reviewed, not verified.</summary>
    Conflicting = 2,
    /// <summary>Documented Concern: the check was REVIEWED, but it is not a successful verification.</summary>
    Concern = 3,
    /// <summary>Documented Verified: reviewed AND successfully verified.</summary>
    Verified = 4
}

/// <summary>Research progress stage of a site, from the number of reviewed core checks.</summary>
public enum ResearchStage { NoneReviewed = 0, PartlyReviewed = 1, AllReviewed = 2 }

/// <summary>Issues that put a site in the research review queue.</summary>
public enum ReviewIssue
{
    ParcelMismatch = 0,
    BusinessConflict = 1,
    ConflictingEvidence = 2,
    UnverifiedContact = 3,
    TruckAccessUnchecked = 4,
    ElectricalUnknown = 5,
    InsufficientEvidence = 6
}

/// <summary>
/// Single source of the research-quality rules (evidence sufficiency, research completeness, review-queue issues,
/// research set). Derived ONLY from research records — never from, and never feeding, the C3 score or rank.
/// The in-memory form and the SQL form (filters) must stay equivalent; tests compare them.
/// </summary>
public static class ResearchQuality
{
    public const int CheckCount = 8;

    /// <summary>Feasibility item behind each core check (all but <see cref="ResearchCheck.VerifiedContact"/>).</summary>
    public static readonly IReadOnlyDictionary<ResearchCheck, FeasibilityItemKey> CheckItems = new Dictionary<ResearchCheck, FeasibilityItemKey>
    {
        [ResearchCheck.ParcelIdentity] = FeasibilityItemKey.ParcelIdentity,
        [ResearchCheck.BusinessAtAddress] = FeasibilityItemKey.BusinessAtAddress,
        [ResearchCheck.TruckEntrance] = FeasibilityItemKey.TruckEntranceAccess,
        [ResearchCheck.TurningSpace] = FeasibilityItemKey.TurningManeuveringSpace,
        [ResearchCheck.ParkingCapacity] = FeasibilityItemKey.TruckParkingCapacity,
        [ResearchCheck.ElectricalService] = FeasibilityItemKey.ElectricalService,
        [ResearchCheck.Zoning] = FeasibilityItemKey.Zoning,
    };

    public static readonly ResearchCheck[] AllChecks = Enum.GetValues<ResearchCheck>();

    public static string Label(ResearchCheck c) => c switch
    {
        ResearchCheck.ParcelIdentity => "Parcel identity",
        ResearchCheck.BusinessAtAddress => "Business at address",
        ResearchCheck.TruckEntrance => "Truck entrance",
        ResearchCheck.TurningSpace => "Turning space",
        ResearchCheck.ParkingCapacity => "Parking capacity",
        ResearchCheck.ElectricalService => "Electrical service",
        ResearchCheck.Zoning => "Zoning",
        _ => "Verified contact"
    };

    public static string Label(ReviewIssue i) => i switch
    {
        ReviewIssue.ParcelMismatch => "Parcel mismatch",
        ReviewIssue.BusinessConflict => "Conflicting business information",
        ReviewIssue.ConflictingEvidence => "Conflicting evidence",
        ReviewIssue.UnverifiedContact => "Unverified contact",
        ReviewIssue.TruckAccessUnchecked => "Truck access not checked",
        ReviewIssue.ElectricalUnknown => "Electrical feasibility unknown",
        _ => "Insufficient evidence"
    };

    // ---------------- in memory ----------------

    /// <summary>Evidence text, a source URL and an evidence date are all present.</summary>
    public static bool IsDocumented(FeasibilityItem f) =>
        !string.IsNullOrEmpty(f.Evidence) && !string.IsNullOrEmpty(f.SourceUrl) && f.EvidenceDate is not null;

    /// <summary>What a verdict lacks to be documented, e.g. "a source URL and an evidence date" (empty when documented).</summary>
    public static string MissingText(FeasibilityItem f)
    {
        var parts = new List<string>();
        if (string.IsNullOrEmpty(f.Evidence)) parts.Add("evidence text");
        if (string.IsNullOrEmpty(f.SourceUrl)) parts.Add("a source URL");
        if (f.EvidenceDate is null) parts.Add("an evidence date");
        return parts.Count <= 1 ? string.Concat(parts) : string.Join(", ", parts[..^1]) + " and " + parts[^1];
    }

    public static CheckState State(FeasibilityItem? f) => f?.Status switch
    {
        null or FeasibilityStatus.Unknown => CheckState.NotStarted,
        FeasibilityStatus.Conflicting => CheckState.Conflicting,
        FeasibilityStatus.Verified => IsDocumented(f) ? CheckState.Verified : CheckState.InsufficientEvidence,
        FeasibilityStatus.Concern => IsDocumented(f) ? CheckState.Concern : CheckState.InsufficientEvidence,
        _ => CheckState.NotStarted
    };

    /// <summary>The contact check: a verified contact with a cited source (any role). A contact marked verified
    /// without a source is flagged, not counted.</summary>
    public static CheckState ContactState(IEnumerable<SiteContact> contacts)
    {
        var list = contacts.ToList();
        if (list.Any(c => c.VerificationStatus == ContactVerificationStatus.Verified && ResearchReadiness.IsSourced(c)))
            return CheckState.Verified;
        return list.Any(c => c.VerificationStatus == ContactVerificationStatus.Verified)
            ? CheckState.InsufficientEvidence
            : CheckState.NotStarted;
    }

    public static bool IsReviewed(CheckState s) => s is CheckState.Verified or CheckState.Concern;

    public static IReadOnlyDictionary<ResearchCheck, CheckState> States(IEnumerable<FeasibilityItem> siteItems, IEnumerable<SiteContact> siteContacts)
    {
        var byKey = siteItems.GroupBy(f => f.ItemKey).ToDictionary(g => g.Key, g => g.First());
        var result = new Dictionary<ResearchCheck, CheckState>();
        foreach (var (check, key) in CheckItems)
            result[check] = State(byKey.GetValueOrDefault(key));
        result[ResearchCheck.VerifiedContact] = ContactState(siteContacts);
        return result;
    }

    public static (int Reviewed, int Verified) Counts(IReadOnlyDictionary<ResearchCheck, CheckState> states) =>
        (states.Values.Count(IsReviewed), states.Values.Count(s => s == CheckState.Verified));

    public static ResearchStage Stage(int reviewed) =>
        reviewed <= 0 ? ResearchStage.NoneReviewed : reviewed >= CheckCount ? ResearchStage.AllReviewed : ResearchStage.PartlyReviewed;

    /// <summary>Open review-queue issues for one site (empty when there is nothing to resolve).</summary>
    public static List<ReviewIssue> Issues(IEnumerable<FeasibilityItem> siteItems, IEnumerable<SiteContact> siteContacts)
    {
        var items = siteItems.ToList();
        var contacts = siteContacts.ToList();
        var byKey = items.GroupBy(f => f.ItemKey).ToDictionary(g => g.Key, g => g.First());
        FeasibilityStatus St(FeasibilityItemKey k) => byKey.TryGetValue(k, out var f) ? f.Status : FeasibilityStatus.Unknown;
        var issues = new List<ReviewIssue>();

        if (St(FeasibilityItemKey.ParcelIdentity) is FeasibilityStatus.Concern or FeasibilityStatus.Conflicting)
            issues.Add(ReviewIssue.ParcelMismatch);
        if (St(FeasibilityItemKey.BusinessAtAddress) is FeasibilityStatus.Concern or FeasibilityStatus.Conflicting)
            issues.Add(ReviewIssue.BusinessConflict);
        if (items.Any(f => f.Status == FeasibilityStatus.Conflicting
                           && f.ItemKey is not (FeasibilityItemKey.ParcelIdentity or FeasibilityItemKey.BusinessAtAddress)))
            issues.Add(ReviewIssue.ConflictingEvidence);
        if (ResearchReadiness.Readiness(contacts) == ContactReadiness.UnverifiedLead)
            issues.Add(ReviewIssue.UnverifiedContact);
        if (St(FeasibilityItemKey.TruckEntranceAccess) == FeasibilityStatus.Unknown)
            issues.Add(ReviewIssue.TruckAccessUnchecked);
        if (St(FeasibilityItemKey.ElectricalService) == FeasibilityStatus.Unknown)
            issues.Add(ReviewIssue.ElectricalUnknown);
        if (items.Any(f => f.Status is FeasibilityStatus.Verified or FeasibilityStatus.Concern && !IsDocumented(f)))
            issues.Add(ReviewIssue.InsufficientEvidence);
        return issues;
    }

    // ---------------- tokens ----------------

    /// <summary>Filter tokens: any (whole research set) | none | partial | complete.</summary>
    public static (bool Any, ResearchStage[] Stages) ParseStages(IEnumerable<string> tokens)
    {
        var list = tokens.Select(t => t.Trim().ToLowerInvariant()).ToList();
        var stages = list.Select(t => t switch
            {
                "none" => (ResearchStage?)ResearchStage.NoneReviewed,
                "partial" => ResearchStage.PartlyReviewed,
                "complete" => ResearchStage.AllReviewed,
                _ => null
            }).Where(x => x is not null).Select(x => x!.Value).Distinct().ToArray();
        return (list.Contains("any"), stages);
    }

    public static ReviewIssue[] ParseIssues(IEnumerable<string> tokens) =>
        tokens.Select(t => Enum.TryParse<ReviewIssue>(t.Trim(), true, out var i) ? (ReviewIssue?)i : null)
            .Where(x => x is not null).Select(x => x!.Value).Distinct().ToArray();

    // ---------------- SQL ----------------

    /// <summary>
    /// The research set: saved (bookmarked) sites, sites with any screening record, and sites with any research
    /// record or history entry (including "Start research"). The review queue and completeness rollups cover only
    /// this set, not all candidates.
    /// </summary>
    public static Expression<Func<CandidateSite, bool>> InResearchSet(ResearchSources src) =>
        s => src.Saved.Any(x => x.SiteId == s.SiteId)
             || src.ScreeningEntries.Any(x => x.SiteId == s.SiteId)
             || src.Screenings.Any(x => x.SiteId == s.SiteId)
             || src.Feasibility.Any(x => x.SiteId == s.SiteId)
             || src.Contacts.Any(x => x.SiteId == s.SiteId)
             || src.History.Any(x => x.SiteId == s.SiteId);

    /// <summary>SQL: research-set sites whose number of reviewed core checks puts them in one of <paramref name="wanted"/>.</summary>
    public static Expression<Func<CandidateSite, bool>> StageIn(IReadOnlyCollection<ResearchStage> wanted, ResearchSources src)
    {
        bool wN = wanted.Contains(ResearchStage.NoneReviewed), wP = wanted.Contains(ResearchStage.PartlyReviewed),
             wC = wanted.Contains(ResearchStage.AllReviewed);
        var feas = src.Feasibility;
        var contacts = src.Contacts;
        const FeasibilityStatus verified = FeasibilityStatus.Verified, concern = FeasibilityStatus.Concern;
        const ContactVerificationStatus cver = ContactVerificationStatus.Verified;
        const FeasibilityItemKey k1 = FeasibilityItemKey.ParcelIdentity, k2 = FeasibilityItemKey.BusinessAtAddress,
            k3 = FeasibilityItemKey.TruckEntranceAccess, k4 = FeasibilityItemKey.TurningManeuveringSpace,
            k5 = FeasibilityItemKey.TruckParkingCapacity, k6 = FeasibilityItemKey.ElectricalService, k7 = FeasibilityItemKey.Zoning;
        const int all = CheckCount;

        // Reviewed = documented Verified or documented Concern on a core item, plus a sourced verified contact.
        Expression<Func<CandidateSite, int>> reviewed = s =>
            feas.Count(f => f.SiteId == s.SiteId
                            && (f.ItemKey == k1 || f.ItemKey == k2 || f.ItemKey == k3 || f.ItemKey == k4 || f.ItemKey == k5 || f.ItemKey == k6 || f.ItemKey == k7)
                            && (f.Status == verified || f.Status == concern)
                            && f.Evidence != null && f.Evidence != "" && f.SourceUrl != null && f.SourceUrl != "" && f.EvidenceDate != null)
            + (contacts.Any(c => c.SiteId == s.SiteId && c.VerificationStatus == cver && c.SourceUrl != null && c.SourceUrl != "") ? 1 : 0);

        var inSet = InResearchSet(src);
        var p = Expression.Parameter(typeof(CandidateSite), "s");
        var count = new Replace(reviewed.Parameters[0], p).Visit(reviewed.Body);
        Expression stageTest = Expression.Constant(false);
        if (wN) stageTest = Expression.OrElse(stageTest, Expression.Equal(count, Expression.Constant(0)));
        if (wP) stageTest = Expression.OrElse(stageTest, Expression.AndAlso(
            Expression.GreaterThan(count, Expression.Constant(0)), Expression.LessThan(count, Expression.Constant(all))));
        if (wC) stageTest = Expression.OrElse(stageTest, Expression.GreaterThanOrEqual(count, Expression.Constant(all)));
        var body = Expression.AndAlso(new Replace(inSet.Parameters[0], p).Visit(inSet.Body), stageTest);
        return Expression.Lambda<Func<CandidateSite, bool>>(body, p);
    }

    private sealed class Replace(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : base.VisitParameter(node);
    }
}

/// <summary>Read-store queryables the research rules need.</summary>
public sealed record ResearchSources(
    IQueryable<SavedSite> Saved,
    IQueryable<SiteScreening> Screenings,
    IQueryable<SiteScreeningEntry> ScreeningEntries,
    IQueryable<FeasibilityItem> Feasibility,
    IQueryable<SiteContact> Contacts,
    IQueryable<ResearchHistoryEntry> History)
{
    public static ResearchSources From(Abstractions.IReadStore r) =>
        new(r.SavedSites, r.SiteScreenings, r.SiteScreeningEntries, r.FeasibilityItems, r.SiteContacts, r.ResearchHistory);
}
