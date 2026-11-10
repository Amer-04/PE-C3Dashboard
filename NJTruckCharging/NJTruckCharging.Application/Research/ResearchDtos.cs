using System.ComponentModel.DataAnnotations;
using NJTruckCharging.Domain.Entities;

namespace NJTruckCharging.Application.Research;

/// <summary>One append-only history entry. <see cref="RecordedBy"/> is self-reported, not authenticated.</summary>
public sealed record ResearchHistoryEntryDto(
    long Id, string SiteId, ResearchSubject Subject, ResearchAction Action,
    FeasibilityItemKey? ItemKey, int? ContactId,
    string? PreviousStatus, string? Status, string? Evidence, string? SourceUrl, DateOnly? EvidenceDate,
    string? Note, string? RecordedBy, DateTimeOffset RecordedAtUtc);

/// <param name="RecordedBy">Self-reported name (free text, not authenticated).</param>
public sealed record StartResearchRequest(
    [Required] string SiteId,
    [MaxLength(2000)] string? Note = null,
    [MaxLength(200)] string? RecordedBy = null);

public sealed record ResearchCheckDto(ResearchCheck Check, string Label, CheckState State, FeasibilityItemKey? ItemKey)
{
    public bool Reviewed => ResearchQuality.IsReviewed(State);
    public bool Verified => State == CheckState.Verified;
}

public sealed record ReviewIssueDto(ReviewIssue Issue, string Label);

/// <summary>
/// Research completeness of one site: how many of the eight core checks are REVIEWED (documented Verified or
/// documented Concern) and how many are VERIFIED (documented Verified only). Independent of the C3 score.
/// </summary>
public sealed record SiteResearchDto(
    string SiteId, bool InResearchSet, int Reviewed, int Verified, int Total, ResearchStage Stage,
    IReadOnlyList<ResearchCheckDto> Checks, IReadOnlyList<ReviewIssueDto> Issues, bool ScreenedUnsuitable);

public sealed record ReviewQueueItemDto(
    int Id, string SiteId, int Segment, int Rank, string Address, string Municipality, string County,
    int Reviewed, int Verified, int Total, IReadOnlyList<ReviewIssueDto> Issues);

/// <param name="ResearchSites">Research-set sites in scope (after the segment filter).</param>
/// <param name="ExcludedUnsuitable">Research-set sites left out because they were screened Unsuitable with evidence.</param>
public sealed record ReviewQueueDto(
    int ResearchSites, int SitesWithIssues, int ExcludedUnsuitable,
    IReadOnlyDictionary<ReviewIssue, int> IssueCounts, IReadOnlyList<ReviewQueueItemDto> Items);

/// <summary>Research completeness for one C3 segment (research-set sites only).</summary>
public sealed record SegmentResearchDto(
    int Segment, int ResearchSites, int NoneReviewed, int PartlyReviewed, int AllReviewed,
    int ChecksReviewed, int ChecksVerified, int ChecksTotal, int SitesWithIssues);

public sealed record ResearchSummaryDto(
    int ResearchSites, int NoneReviewed, int PartlyReviewed, int AllReviewed,
    int ChecksReviewed, int ChecksVerified, int ChecksTotal, int SitesWithIssues,
    IReadOnlyList<SegmentResearchDto> Segments);
