using NJTruckCharging.Application.Screening;
using NJTruckCharging.Domain.Entities;

namespace NJTruckCharging.Application.Sites;

/// <summary>Filters shared by the map, the ranked list, the KPIs and the CSV export (bound from the query string).</summary>
public sealed class SiteFilter
{
    /// <summary>
    /// PRELIMINARY screening factor only: assumed trailer-equivalent stalls per usable (open) acre. This is
    /// a rough desktop estimate for narrowing candidates — NOT a verified, buildable parking count. It
    /// assumes the open acreage could hold trailer parking (it may not all be usable or trailer-suitable)
    /// and is independent of the C3 score. Verified parking comes from the feasibility review.
    /// </summary>
    public const double EstimatedTrailersPerOpenAcre = 15;

    public int[]? Segments { get; set; }
    public double? MinScore { get; set; }
    /// <summary>Only the top N sites of each segment (by computed rank).</summary>
    public int? TopN { get; set; }
    /// <summary>industrial | vacant | commercial</summary>
    public string[]? PropertyClasses { get; set; }
    public double? MinOpenAcres { get; set; }
    public double? MaxMilesToExit { get; set; }
    public bool? FreightOnSite { get; set; }
    public bool ExcludeFloodZone { get; set; }
    public bool OnlyFloodChecked { get; set; }
    public bool ExcludeHighlandsPreservation { get; set; }
    public bool ExcludeContaminated { get; set; }
    public bool ExcludeNearPowerEdison { get; set; }
    public bool ExcludeBusyMultiTenant { get; set; }
    public string? Utility { get; set; }
    public string? County { get; set; }
    public string? Highway { get; set; }
    /// <summary>Matches address, municipality, businesses, site id or block/lot.</summary>
    public string? Search { get; set; }

    // ---- Business-owner screening filters ----
    /// <summary>IOS classifications to include: confirmed (manual review) | indicated (freight signal) | potential | not | unknown. See <see cref="IosRules"/>.</summary>
    public string[]? IosClasses { get; set; }
    /// <summary>Minimum PRELIMINARY estimated trailer parking (derived from open acres); see <see cref="EstimatedTrailersPerOpenAcre"/>.</summary>
    public int? MinEstimatedParking { get; set; }
    /// <summary>Only sites where the feasibility review verified truck entrance / access.</summary>
    public bool TruckEntranceVerified { get; set; }
    /// <summary>Only sites where the feasibility review verified electrical service.</summary>
    public bool ElectricalVerified { get; set; }
    /// <summary>Only sites where the feasibility review verified truck parking capacity.</summary>
    public bool ParkingVerified { get; set; }
    /// <summary>Only sites with a verified decision-maker contact on record.</summary>
    public bool OnlyWithVerifiedContact { get; set; }
    /// <summary>Screening decisions to include: promising | research | unsuitable | notreviewed.</summary>
    public string[]? ScreeningDecisions { get; set; }
    /// <summary>Site Evidence states to include: verified | insufficient | preliminary | mismatch | unknown. See <see cref="ResearchReadiness"/>.</summary>
    public string[]? EvidenceStates { get; set; }
    /// <summary>Contact Readiness states to include: relevant | general | lead | none. See <see cref="ResearchReadiness"/>.</summary>
    public string[]? ContactStates { get; set; }
    /// <summary>Research completeness stages (research-set sites only): any | none | partial | complete.
    /// See <see cref="Research.ResearchQuality"/>. Independent of the score.</summary>
    public string[]? ResearchStages { get; set; }

    /// <summary>
    /// Applies the filter. Pass <paramref name="feasibility"/> and <paramref name="contacts"/> (the
    /// corresponding read-store queryables) to enable the evaluation-state filters; when null those
    /// filters are skipped.
    /// </summary>
    public IQueryable<CandidateSite> Apply(
        IQueryable<CandidateSite> q,
        IQueryable<FeasibilityItem>? feasibility = null,
        IQueryable<SiteContact>? contacts = null,
        IQueryable<SiteScreening>? screenings = null,
        Research.ResearchSources? research = null)
    {
        if (Segments is { Length: > 0 }) q = q.Where(s => Segments.Contains(s.Segment));
        if (MinScore is { } min) q = q.Where(s => s.Score >= min);
        if (TopN is > 0) q = q.Where(s => s.ComputedRank <= TopN);
        if (MinOpenAcres is { } acres) q = q.Where(s => s.OpenAcresEst >= acres);
        if (MaxMilesToExit is { } miles) q = q.Where(s => s.MilesToExit <= miles);
        if (FreightOnSite is { } freight) q = q.Where(s => s.FreightUse == freight);
        if (ExcludeFloodZone) q = q.Where(s => !s.InFloodZone);
        if (OnlyFloodChecked) q = q.Where(s => s.FloodChecked);
        if (ExcludeHighlandsPreservation) q = q.Where(s => !s.InHighlandsPreservation);
        if (ExcludeContaminated) q = q.Where(s => !s.IsContaminated);
        if (ExcludeNearPowerEdison) q = q.Where(s => !s.NearPowerEdisonSite);
        if (ExcludeBusyMultiTenant) q = q.Where(s => !s.BusyMultiTenant);
        if (!string.IsNullOrWhiteSpace(Utility)) q = q.Where(s => s.Utility == Utility);
        if (!string.IsNullOrWhiteSpace(County)) q = q.Where(s => s.County == County);
        if (!string.IsNullOrWhiteSpace(Highway)) q = q.Where(s => s.NearestHighway == Highway);

        // IOS classification (centralised in IosRules; manual outdoor-storage review overrides source data).
        if (IosClasses is { Length: > 0 })
            q = q.Where(IosRules.InClasses(IosRules.ParseTokens(IosClasses), feasibility));

        // Preliminary estimated parking (not verified); excludes sites with no open-acreage estimate.
        if (MinEstimatedParking is { } minPark)
            q = q.Where(s => s.OpenAcresEst > 0 && s.OpenAcresEst * EstimatedTrailersPerOpenAcre >= minPark);

        // Evaluation-state filters (require the related queryables).
        if (feasibility is not null)
        {
            // "Verified" = a documented Verified verdict (evidence text, source URL, evidence date) — the same rule as
            // Research.ResearchQuality.IsDocumented; undocumented verdicts are "Insufficient evidence", not verified.
            const FeasibilityStatus ver = FeasibilityStatus.Verified;
            if (TruckEntranceVerified)
                q = q.Where(s => feasibility.Any(f => f.SiteId == s.SiteId && f.ItemKey == FeasibilityItemKey.TruckEntranceAccess && f.Status == ver
                    && f.Evidence != null && f.Evidence != "" && f.SourceUrl != null && f.SourceUrl != "" && f.EvidenceDate != null && f.Reviewer != null && f.Reviewer != ""));
            if (ElectricalVerified)
                q = q.Where(s => feasibility.Any(f => f.SiteId == s.SiteId && f.ItemKey == FeasibilityItemKey.ElectricalService && f.Status == ver
                    && f.Evidence != null && f.Evidence != "" && f.SourceUrl != null && f.SourceUrl != "" && f.EvidenceDate != null && f.Reviewer != null && f.Reviewer != ""));
            if (ParkingVerified)
                q = q.Where(s => feasibility.Any(f => f.SiteId == s.SiteId && f.ItemKey == FeasibilityItemKey.TruckParkingCapacity && f.Status == ver
                    && f.Evidence != null && f.Evidence != "" && f.SourceUrl != null && f.SourceUrl != "" && f.EvidenceDate != null && f.Reviewer != null && f.Reviewer != ""));
        }
        if (screenings is not null && ScreeningDecisions is { Length: > 0 })
            q = q.Where(ScreeningRules.InDecisions(ScreeningRules.ParseTokens(ScreeningDecisions), screenings));
        // A "verified decision-maker" is a sourced, verified contact in a decision-making role (same rule as Contact Readiness).
        if (contacts is not null && OnlyWithVerifiedContact)
            q = q.Where(ResearchReadiness.ReadinessIn([ContactReadiness.RelevantContactVerified], contacts));
        if (contacts is not null && ContactStates is { Length: > 0 })
            q = q.Where(ResearchReadiness.ReadinessIn(ResearchReadiness.ParseReadiness(ContactStates), contacts));
        if (research is not null && ResearchStages is { Length: > 0 })
        {
            var (any, stages) = Research.ResearchQuality.ParseStages(ResearchStages);
            if (any) q = q.Where(Research.ResearchQuality.InResearchSet(research));
            else if (stages.Length > 0) q = q.Where(Research.ResearchQuality.StageIn(stages, research));
        }
        if (feasibility is not null && screenings is not null && EvidenceStates is { Length: > 0 })
            q = q.Where(ResearchReadiness.EvidenceIn(ResearchReadiness.ParseEvidence(EvidenceStates), feasibility, screenings));

        if (PropertyClasses is { Length: > 0 })
        {
            var ind = PropertyClasses.Contains("industrial", StringComparer.OrdinalIgnoreCase);
            var vac = PropertyClasses.Contains("vacant", StringComparer.OrdinalIgnoreCase);
            var com = PropertyClasses.Contains("commercial", StringComparer.OrdinalIgnoreCase);
            // A merged site's class is its best class: 4B > 1 > 4A.
            q = q.Where(s =>
                (ind && s.PropClass.Contains("4B")) ||
                (vac && !s.PropClass.Contains("4B") && (s.PropClass == "1" || s.PropClass.StartsWith("1/"))) ||
                (com && !s.PropClass.Contains("4B") && !(s.PropClass == "1" || s.PropClass.StartsWith("1/"))));
        }

        if (!string.IsNullOrWhiteSpace(Search))
        {
            // Case-insensitive on every database (SQL Server's collation already is; PostgreSQL's is not).
            var term = Search.Trim().ToUpperInvariant();
            q = q.Where(s =>
                s.SiteAddress.ToUpper().Contains(term) ||
                s.Municipality.ToUpper().Contains(term) ||
                s.SiteId.ToUpper().Contains(term) ||
                (s.BusinessesOnSite != null && s.BusinessesOnSite.ToUpper().Contains(term)) ||
                (s.OwnerMailAddress != null && s.OwnerMailAddress.ToUpper().Contains(term)));
        }
        return q;
    }
}

public enum SiteSort { Score, Rank, OpenAcres, MilesToExit, TrucksBased5Mi, TruckTraffic, MilesToPowerEdison }

public sealed class SiteListQuery
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public SiteSort SortBy { get; set; } = SiteSort.Score;
    public bool Descending { get; set; } = true;
}
