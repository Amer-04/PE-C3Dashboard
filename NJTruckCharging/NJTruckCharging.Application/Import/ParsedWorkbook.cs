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
    TimeSpan Duration,
    /// <summary>Methodology version whose scores are live after the import.</summary>
    int? MethodologyVersionId = null,
    /// <summary>True when a custom (non-baseline) active methodology was re-applied to the new data.</summary>
    bool CustomMethodologyReapplied = false);

/// <summary>The workbook (or the active methodology) failed validation; nothing was imported.</summary>
public sealed class ImportValidationException(string message) : Exception(message);
