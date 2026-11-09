using NJTruckCharging.Domain.Entities;

namespace NJTruckCharging.Application.Comparison;

/// <summary>Outcome of a desktop/source-data screen (NOT a field verification).</summary>
public enum ScreeningStatus
{
    /// <summary>The source dataset did not screen this site — unknown, never treated as clear.</summary>
    NotChecked = 0,
    Clear = 1,
    Caution = 2,
    Flagged = 3
}

/// <summary>A reviewer's feasibility verdict on one item. Unknown = not yet reviewed (never "verified").</summary>
public sealed record VerificationDto(FeasibilityStatus Status, string? Evidence, DateTimeOffset? ReviewedAtUtc)
{
    /// <summary>Evidence text, source URL and evidence date are all present (Research.ResearchQuality.IsDocumented).</summary>
    public bool Documented { get; init; }
}

/// <summary>A source-data screen result with a plain-language detail.</summary>
public sealed record ScreeningDto(ScreeningStatus Status, string Detail);

public sealed record ComparedIosDto(
    IosClassification Classification, IosBasis Basis, string Evidence,
    IosClassification DerivedClassification, FeasibilityStatus ReviewStatus);

/// <summary>One site in a comparison. No CRM or sales fields.</summary>
public sealed record ComparedSiteDto
{
    public required string SiteId { get; init; }
    public required int CandidateId { get; init; }
    public required string Address { get; init; }
    public required string Municipality { get; init; }
    public required string County { get; init; }

    public required int Segment { get; init; }
    public required string SegmentCounties { get; init; }
    /// <summary>C3 score — scored and ranked WITHIN its segment; not a cross-segment ranking.</summary>
    public required double Score { get; init; }
    public required int SegmentRank { get; init; }
    public required int SitesInSegment { get; init; }

    public required string PropClass { get; init; }
    public required string PropertyClassLabel { get; init; }
    public required double TotalAcres { get; init; }
    /// <summary>Estimated open (unbuilt) acreage; null = no estimate.</summary>
    public double? OpenAcresEst { get; init; }

    public required ComparedIosDto Ios { get; init; }

    public string? NearestHighway { get; init; }
    public string? NearestExit { get; init; }
    public string? ExitName { get; init; }
    public required double MilesToExit { get; init; }
    public required VerificationDto HighwayAccess { get; init; }
    public required VerificationDto TruckEntrance { get; init; }

    /// <summary>PRELIMINARY screening estimate (~15 trailers per open acre); null = Unknown.</summary>
    public int? EstimatedParkingPreliminary { get; init; }
    /// <summary>Reviewer's parking-capacity verdict; evidence holds the verified count when Verified.</summary>
    public required VerificationDto VerifiedParking { get; init; }

    public string? Utility { get; init; }
    public required VerificationDto Electrical { get; init; }

    public required ScreeningDto Flood { get; init; }
    public required ScreeningDto Contamination { get; init; }
    public required ScreeningDto Highlands { get; init; }
    public required VerificationDto FloodEnvironmentalReview { get; init; }

    /// <summary>Feasibility checks not yet reviewed + source screens never run.</summary>
    public required IReadOnlyList<string> MissingVerification { get; init; }
    /// <summary>Feasibility checks a reviewer flagged as a concern (with evidence).</summary>
    public required IReadOnlyList<string> Concerns { get; init; }

    /// <summary>The team's screening decision (suitability judgement) — shown, never used to rank.</summary>
    public required ScreeningDecision ScreeningDecision { get; init; }
    public string? ScreeningEvidence { get; init; }

    public required double Lat { get; init; }
    public required double Lng { get; init; }
    public string GoogleMapsUrl => Sites.SiteLinks.Maps(Lat, Lng);
    public string SatelliteUrl => Sites.SiteLinks.Satellite(Lat, Lng);
    public string StreetViewUrl => Sites.SiteLinks.StreetView(Lat, Lng);
}

/// <summary>
/// One comparison attribute across all sites. <see cref="Values"/> and <see cref="Tones"/> line up with
/// <see cref="SiteComparisonDto.Sites"/>. Tones: verified | concern | unknown | source | clear | caution | flag | "".
/// <see cref="Differs"/> only flags that values differ — it never declares a better site.
/// </summary>
public sealed record ComparisonRowDto(
    string Key, string Group, string Label, string? Hint,
    IReadOnlyList<string> Values, IReadOnlyList<string> Tones, bool Comparable, bool Differs);

public sealed record SiteComparisonDto(
    IReadOnlyList<ComparedSiteDto> Sites,
    IReadOnlyList<ComparisonRowDto> Rows,
    IReadOnlyList<string> MissingSiteIds,
    bool MixedSegments,
    string ScoreNote);
