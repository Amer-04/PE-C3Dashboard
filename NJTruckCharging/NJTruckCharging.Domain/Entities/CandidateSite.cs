using NJTruckCharging.Domain.Scoring;

namespace NJTruckCharging.Domain.Entities;

/// <summary>
/// One candidate site (a parcel, or several adjacent parcels merged because they share an owner)
/// screened for the NJ C3 Medium &amp; Heavy-Duty EV charging NOFA. Mirrors the
/// "All candidates" sheet of C3_candidate_sites_all_segments.xlsx.
/// </summary>
public class CandidateSite
{
    public int Id { get; set; }

    // ---- Identity -------------------------------------------------------------
    public string SiteId { get; set; } = default!;          // site_id (unique)
    public string PamsPin { get; set; } = default!;         // pams_pin
    public int Segment { get; set; }                        // 1..6 (NOFA Table 2)
    public int RankInSegment { get; set; }                  // rank_in_segment (from file)
    public double FileScore { get; set; }                   // score as delivered in the workbook

    // ---- Location ---------------------------------------------------------------
    public string SiteAddress { get; set; } = default!;
    public string Municipality { get; set; } = default!;
    public string County { get; set; } = default!;
    public string? Block { get; set; }
    public string? Lot { get; set; }
    public string? Qual { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }

    // ---- Parcel -------------------------------------------------------------------
    public int LotsInSite { get; set; }
    public string? MemberLots { get; set; }
    public double Acres { get; set; }
    public double RecordedAcres { get; set; }
    public double OpenAcresEst { get; set; }
    public double BuildingCoverage { get; set; }
    public string PropClass { get; set; } = default!;       // 1 vacant, 4A commercial, 4B industrial (or combos "1/4B")
    public string? Zoning { get; set; }
    public double ShortSideFt { get; set; }
    public double Compactness { get; set; }
    public string? BuildingDescription { get; set; }
    public string? LandDescription { get; set; }
    public int? YearBuilt { get; set; }
    public double LandValue { get; set; }
    public double ImprovementValue { get; set; }
    public double AnnualTax { get; set; }
    public double LastSalePrice { get; set; }
    public string? LastDeedDate { get; set; }

    // ---- Owner (names are not public in NJ – Daniel's Law) -----------------------------
    public string? OwnerName { get; set; }
    public string? OwnerMailAddress { get; set; }
    public string? OwnerMailCity { get; set; }
    public string? OwnerMailZip { get; set; }
    public bool OwnerOccupiedHint { get; set; }

    // ---- Businesses on the parcel ------------------------------------------------------
    public string? BusinessesOnSite { get; set; }
    public string? BusinessCategories { get; set; }
    public string? BusinessPhone { get; set; }
    public string? BusinessWebsite { get; set; }
    public bool FreightUse { get; set; }
    public bool IndustrialUse { get; set; }

    // ---- Truck demand ----------------------------------------------------------------
    public string? DemandSummary { get; set; }
    public int TrucksBased3Mi { get; set; }
    public int TrucksBased5Mi { get; set; }
    public int TrucksBased10Mi { get; set; }
    public int CarriersBased5Mi { get; set; }
    public int TractorsBased5Mi { get; set; }
    public int TruckAadtAdjacent { get; set; }
    public int TruckAadtAtExit { get; set; }
    public double MilesToTruckRoad { get; set; }
    public int LargeWarehouses2Mi { get; set; }
    public int TruckStops2Mi { get; set; }
    public double MilesToTruckStop { get; set; }
    public double MilesToPort { get; set; }
    public string? NearestPort { get; set; }

    // ---- Highway access ----------------------------------------------------------------
    public string? NearestHighway { get; set; }
    public string? NearestExit { get; set; }
    public string? ExitName { get; set; }
    public double MilesToExit { get; set; }

    // ---- Existing chargers -------------------------------------------------------------
    public double MilesToTruckDcfc { get; set; }
    public double MilesToAnyDcfc { get; set; }
    public string? NearestDcfc { get; set; }

    // ---- Power Edison / EV Edison proximity -------------------------------------------
    public string? NearestPowerEdisonSite { get; set; }
    public double MilesToPowerEdisonSite { get; set; }

    // ---- Constraints --------------------------------------------------------------------
    public string? Utility { get; set; }
    public string? Highlands { get; set; }                   // "Highlands Preservation Area" / "Highlands Planning Area"
    public string? FloodZone100Yr { get; set; }              // null = not checked (outside the top 150), "none" = checked & clear
    public int? KnownContaminatedSites { get; set; }         // null = not checked

    /// <summary>
    /// Six or more businesses on the parcel. The workbook does not carry the count (it lists up to 3 names),
    /// so the importer recovers this flag from the delivered score.
    /// </summary>
    public bool BusyMultiTenant { get; set; }

    // ---- Derived flags (persisted so they can be filtered in SQL; call RefreshDerivedFlags after changes) ----
    public bool FloodChecked { get; set; }
    public bool InFloodZone { get; set; }
    public bool InHighlandsPreservation { get; set; }
    public bool IsContaminated { get; set; }
    public bool NearPowerEdisonSite { get; set; }

    public void RefreshDerivedFlags()
    {
        FloodChecked = FloodZone100Yr is not null;
        InFloodZone = FloodChecked && !string.Equals(FloodZone100Yr, "none", StringComparison.OrdinalIgnoreCase);
        InHighlandsPreservation = Highlands?.Contains("Preservation", StringComparison.OrdinalIgnoreCase) == true;
        IsContaminated = KnownContaminatedSites > 0;
        NearPowerEdisonSite = MilesToPowerEdisonSite <= ScoringRules.PowerEdisonConflictMiles;
    }

    // ---- Score computed by the app's scoring engine --------------------------------------
    public ScoreBreakdown Breakdown { get; set; } = new();
    public double Score { get; set; }                        // == Breakdown.Total
    public int ComputedRank { get; set; }                    // rank by Score within the segment

    /// <summary>Methodology version whose results are materialised in Score / ComputedRank / Breakdown (the active one).</summary>
    public int? ScoreVersionId { get; set; }
    /// <summary>Sum of the active methodology's "Truck demand" criteria (persisted: custom criteria have no column).</summary>
    public double TruckDemandPoints { get; set; }

    public List<NearbyFleet> NearbyFleets { get; set; } = [];
}
