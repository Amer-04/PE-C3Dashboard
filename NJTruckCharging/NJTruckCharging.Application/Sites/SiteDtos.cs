using NJTruckCharging.Domain.Entities;
using NJTruckCharging.Domain.Scoring;

namespace NJTruckCharging.Application.Sites;

/// <summary>Compact point for the map layer (sent for every filtered site).</summary>
public sealed record SiteMapPointDto(
    int Id, double Lat, double Lng, double Score, int Segment, int Rank,
    double OpenAcres, bool Freight, bool Flood, bool NearPe);

public sealed record SiteListItemDto(
    int Id, string SiteId, int Segment, int Rank, double Score, double FileScore,
    string Address, string Municipality, string County, string PropClass,
    double Acres, double OpenAcres, double MilesToExit, string? NearestHighway, string? NearestExit,
    int TrucksBased5Mi, int TruckTraffic, int LargeWarehouses2Mi,
    string? Businesses, bool Freight, string? Utility,
    bool InFloodZone, bool FloodChecked, bool IsContaminated, bool InHighlandsPreservation,
    bool NearPowerEdison, string? NearestPowerEdisonSite, double MilesToPowerEdison,
    double Lat, double Lng,
    int? EstimatedParking, IosClassification IosClassification,
    IosBasis IosBasis = IosBasis.SourceDerived,
    ScreeningDecision ScreeningDecision = ScreeningDecision.NotReviewed,
    SiteEvidence SiteEvidence = SiteEvidence.Unknown,
    ContactReadiness ContactReadiness = ContactReadiness.NoContact,
    TopContactDto? TopContact = null,
    /// <summary>Business phone listed in the C3 source data (general business line; not a verified decision-maker).</summary>
    string? BusinessPhone = null,
    // Owner mailing address from the C3 parcel record (same fields as Site Details → "Who to contact").
    string? OwnerMailAddress = null, string? OwnerMailCity = null, string? OwnerMailZip = null,
    /// <summary>True only when a reviewer verified (with evidence) that the listed business operates at the address.
    /// The listed phone itself is still unverified.</summary>
    bool BusinessListingConfirmed = false);

public sealed record ScoreLineDto(string Key, string Label, string Group, double Points, double Max, double Min, string Basis);

public sealed record NearbyFleetDto(
    string Carrier, string? Dba, int Trucks, int Tractors, int Drivers, string? Address,
    double ApproxMiles, string? Phone, string? Email, string? UsDot, string? Operation, string? Cargo);

public sealed record SiteDetailDto
{
    public required SiteListItemDto Summary { get; init; }
    public required IReadOnlyList<ScoreLineDto> ScoreLines { get; init; }
    public required double TruckDemandPoints { get; init; }
    public required int SitesInSegment { get; init; }

    public string? PamsPin { get; init; }
    public string? Block { get; init; }
    public string? Lot { get; init; }
    public string? Qual { get; init; }
    public int LotsInSite { get; init; }
    public string? MemberLots { get; init; }
    public double RecordedAcres { get; init; }
    public double BuildingCoverage { get; init; }
    public string? Zoning { get; init; }
    public double ShortSideFt { get; init; }
    public double Compactness { get; init; }
    public string? BuildingDescription { get; init; }
    public string? LandDescription { get; init; }
    public int? YearBuilt { get; init; }
    public double LandValue { get; init; }
    public double ImprovementValue { get; init; }
    public double AnnualTax { get; init; }
    public double LastSalePrice { get; init; }
    public string? LastDeedDate { get; init; }

    public string? OwnerName { get; init; }
    public string? OwnerMailAddress { get; init; }
    public string? OwnerMailCity { get; init; }
    public string? OwnerMailZip { get; init; }
    public bool OwnerOccupiedHint { get; init; }

    public string? BusinessCategories { get; init; }
    public string? BusinessPhone { get; init; }
    public string? BusinessWebsite { get; init; }
    public bool IndustrialUse { get; init; }
    public bool BusyMultiTenant { get; init; }

    public string? DemandSummary { get; init; }
    public int TrucksBased3Mi { get; init; }
    public int TrucksBased10Mi { get; init; }
    public int CarriersBased5Mi { get; init; }
    public int TractorsBased5Mi { get; init; }
    public int TruckAadtAdjacent { get; init; }
    public int TruckAadtAtExit { get; init; }
    public double MilesToTruckRoad { get; init; }
    public int TruckStops2Mi { get; init; }
    public double MilesToTruckStop { get; init; }
    public double MilesToPort { get; init; }
    public string? NearestPort { get; init; }
    public string? ExitName { get; init; }

    public double MilesToTruckDcfc { get; init; }
    public double MilesToAnyDcfc { get; init; }
    public string? NearestDcfc { get; init; }

    public string? Highlands { get; init; }
    public string? FloodZone100Yr { get; init; }
    public int? KnownContaminatedSites { get; init; }

    // ---- Site evaluation (research & feasibility), surfaced for screening ----
    /// <summary>Human-readable reasoning for the effective IOS classification (manual review or source data).</summary>
    public string IosEvidence { get; init; } = "";
    /// <summary>What the source data alone indicates (shown alongside a manual verdict).</summary>
    public IosClassification IosDerivedClassification { get; init; }
    public string IosDerivedEvidence { get; init; } = "";
    /// <summary>Reviewer's outdoor-storage-use verdict (Unknown = not manually reviewed).</summary>
    public FeasibilityStatus IosReviewStatus { get; init; } = FeasibilityStatus.Unknown;
    /// <summary>Feasibility verdict for truck entrance/access: Verified | Concern | Unknown (not yet reviewed).</summary>
    public FeasibilityStatus TruckEntranceStatus { get; init; } = FeasibilityStatus.Unknown;
    public string? TruckEntranceEvidence { get; init; }
    public FeasibilityStatus ElectricalStatus { get; init; } = FeasibilityStatus.Unknown;
    public string? ElectricalEvidence { get; init; }
    /// <summary>Feasibility verdict for truck parking capacity — the VERIFIED parking signal (vs the preliminary estimate).</summary>
    public FeasibilityStatus ParkingStatus { get; init; } = FeasibilityStatus.Unknown;
    /// <summary>Reviewer's verified parking evidence (e.g. "48 marked trailer stalls"), when reviewed.</summary>
    public string? VerifiedParkingEvidence { get; init; }
    public int VerifiedContactCount { get; init; }
    public int TotalContactCount { get; init; }

    /// <summary>Plain-language answers to the business owner's questions (identity, highway, land, parking, truck
    /// spaces, access, power, contact) plus what remains to be verified. Informs only — never ranks or decides.</summary>
    public SiteSuitabilityDto? Suitability { get; init; }

    /// <summary>Parcel, business-at-address, owner/representative and suitability kept apart, with evidence links,
    /// checked dates and discrepancies. Derived from research records only.</summary>
    public SiteValidationDto? Validation { get; init; }

    // ---- Screening decision (suitability, team judgement; independent of the C3 score) ----
    public string? ScreeningEvidence { get; init; }
    public DateTimeOffset? ScreeningUpdatedAtUtc { get; init; }

    public string GoogleMapsUrl => $"https://www.google.com/maps/search/?api=1&query={Summary.Lat},{Summary.Lng}";
    /// <summary>Google Maps centred on the site in satellite imagery, zoomed to parcel level.</summary>
    public string SatelliteUrl => SiteLinks.Satellite(Summary.Lat, Summary.Lng);
    public string StreetViewUrl => $"https://www.google.com/maps/@?api=1&map_action=pano&viewpoint={Summary.Lat},{Summary.Lng}";

    public IReadOnlyList<NearbyFleetDto> NearbyFleets { get; init; } = [];
}

/// <summary>External map links (invariant-culture coordinates).</summary>
public static class SiteLinks
{
    private static string C(double v) => v.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
    public static string Maps(double lat, double lng) => $"https://www.google.com/maps/search/?api=1&query={C(lat)},{C(lng)}";
    public static string Satellite(double lat, double lng) =>
        $"https://www.google.com/maps/@?api=1&map_action=map&center={C(lat)},{C(lng)}&zoom=19&basemap=satellite";
    public static string StreetView(double lat, double lng) =>
        $"https://www.google.com/maps/@?api=1&map_action=pano&viewpoint={C(lat)},{C(lng)}";
}
