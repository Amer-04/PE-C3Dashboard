using NJTruckCharging.Domain.Entities;

namespace NJTruckCharging.Application.Import;

public sealed class ParsedWorkbook
{
    public List<CandidateSite> Sites { get; } = [];
    public List<NearbyFleet> NearbyFleets { get; } = [];       // linked to sites via Segment + RankInSegment
    public List<SegmentInfo> Segments { get; } = [];
    public List<Interchange> Interchanges { get; } = [];
    public List<MethodologyNote> MethodologyNotes { get; } = [];
}

public sealed record ImportResultDto(
    int Sites,
    int NearbyFleets,
    int LinkedFleets,
    int Segments,
    int Interchanges,
    int BusyMultiTenantFlagsRecovered,
    double MaxScoreDifference,
    int RankMismatches,
    TimeSpan Duration);
