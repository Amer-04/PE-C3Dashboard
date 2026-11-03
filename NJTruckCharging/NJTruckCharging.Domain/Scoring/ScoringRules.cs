namespace NJTruckCharging.Domain.Scoring;

/// <summary>
/// Weights and thresholds of the C3 site-scoring algorithm (max ≈ 110). Defaults reproduce the workbook's
/// scores exactly (±0.05 rounding) for all 30,584 candidates.
/// </summary>
public sealed record ScoringRules
{
    public static ScoringRules Default { get; } = new();

    /// <summary>Candidates within this distance of a Power Edison site compete with it (NJDEP may fund only one).</summary>
    public const double PowerEdisonConflictMiles = 2.0;

    // Distance to eligible exit: full points within FullMiles, linear to 0 at ZeroMiles.
    public double ExitMax { get; init; } = 20;
    public double ExitFullMiles { get; init; } = 0.5;
    public double ExitZeroMiles { get; init; } = 5.0;

    // Open land (lot minus building footprints)
    public double OpenLand3Ac { get; init; } = 20;      // >= 3 ac
    public double OpenLand1_5Ac { get; init; } = 16;    // 1.5–3 ac
    public double OpenLand1Ac { get; init; } = 10;      // 1–1.5 ac
    public double OpenLand0_6Ac { get; init; } = 3;     // 0.6–1 ac
    public double OpenLandTiny { get; init; } = -15;    // < 0.6 ac

    // Property class (best class among merged lots)
    public double ClassIndustrial { get; init; } = 15;  // 4B
    public double ClassVacant { get; init; } = 11;      // 1
    public double ClassCommercial { get; init; } = 8;   // 4A

    // Trucking on site
    public double FreightOnSite { get; init; } = 10;
    public double OtherIndustrialOnSite { get; init; } = 4;

    // Truck demand (35)
    public double FleetsMax { get; init; } = 12;        // percentile of trucks based within 5 mi
    public double TrafficMax { get; init; } = 10;       // percentile of max(adjacent, exit) tractor-trailer AADT
    public double WarehousesMax { get; init; } = 6;     // percentile of ≥200k sq ft buildings within 2 mi
    public double TruckStopPoints { get; init; } = 3;   // any truck stop/parking within 2 mi
    public double PortWithin5Mi { get; init; } = 4;
    public double PortWithin10Mi { get; init; } = 2;

    // Charger gaps
    public double NoTruckChargerFar { get; init; } = 10;   // nearest MHD charger > 10 mi
    public double NoTruckChargerNear { get; init; } = 4;   // otherwise
    public double NoTruckChargerMiles { get; init; } = 10;
    public double NoFastCharger { get; init; } = 5;        // nearest DCFC > 2 mi
    public double NoFastChargerMiles { get; init; } = 2;

    // Lot shape
    public double ShapeGood { get; init; } = 5;            // compactness >= 0.5 and short side >= 180 ft
    public double ShapeNarrow { get; init; } = -10;        // short side < 120 ft
    public double ShapeMinCompactness { get; init; } = 0.5;
    public double ShapeGoodShortSideFt { get; init; } = 180;
    public double ShapeNarrowShortSideFt { get; init; } = 120;

    // Penalties
    public double FarFromTruckRoadPenalty { get; init; } = -10;  // > 0.5 mi from a 500+ tractor-trailer/day road
    public double FarFromTruckRoadMiles { get; init; } = 0.5;
    public double HighlandsPreservationPenalty { get; init; } = -15;
    public double BusyMultiTenantPenalty { get; init; } = -5;
    public double FloodPenalty { get; init; } = -10;

    /// <summary>
    /// Flood zones were only checked for the top 150 sites of each segment. The workbook applies the flood
    /// penalty to every unchecked site so they stay ranked below the verified shortlist.
    /// </summary>
    public bool PenalizeUncheckedFlood { get; init; } = true;
}
