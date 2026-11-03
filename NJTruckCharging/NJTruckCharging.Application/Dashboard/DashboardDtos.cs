namespace NJTruckCharging.Application.Dashboard;

public sealed record KpiDto(
    int Sites, int TotalSites, double AverageScore, double TopScore, double MedianOpenAcres,
    int FreightOnSite, int InFloodZone, int Contaminated, int NearPowerEdison, int HighlandsPreservation,
    int WithinHalfMileOfExit);

public sealed record SegmentStatDto(
    int Segment, string Counties, int Sites, double AverageScore, double TopScore,
    int? TopSiteId, string? TopSiteAddress, string? TopSiteMunicipality, int FreightOnSite, int NearPowerEdison,
    string? PowerEdisonSites);

public sealed record BucketDto(string Label, int Count);

public sealed record DashboardSummaryDto(
    KpiDto Kpis,
    IReadOnlyList<SegmentStatDto> Segments,
    IReadOnlyList<BucketDto> ScoreHistogram,
    IReadOnlyList<BucketDto> PropertyClasses,
    IReadOnlyList<BucketDto> Utilities,
    IReadOnlyList<BucketDto> Highways,
    int DaysToProposalDeadline,
    DateOnly ProposalDeadline);
