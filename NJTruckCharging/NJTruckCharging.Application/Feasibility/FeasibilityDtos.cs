using System.ComponentModel.DataAnnotations;
using NJTruckCharging.Application.Research;
using NJTruckCharging.Domain.Entities;

namespace NJTruckCharging.Application.Feasibility;

/// <summary>
/// One checklist line (always present for every item, defaulting to Unknown). <see cref="Reviewer"/> is the
/// self-reported "Recorded by" text (not authenticated). <see cref="EvidenceState"/> says whether the verdict is
/// documented: a Verified or Concern verdict without evidence text, source URL and evidence date is
/// <see cref="CheckState.InsufficientEvidence"/> and counts as neither reviewed nor verified.
/// </summary>
public sealed record FeasibilityItemDto(
    FeasibilityItemKey ItemKey,
    FeasibilityStatus Status,
    string? Evidence,
    string? Reviewer,
    DateTimeOffset? ReviewedAtUtc,
    DateTimeOffset? UpdatedAtUtc)
{
    public string? SourceUrl { get; init; }
    public DateOnly? EvidenceDate { get; init; }
    public CheckState EvidenceState { get; init; }
}

/// <param name="SourceUrl">Where the evidence comes from. Needed (with <paramref name="EvidenceDate"/> and evidence
/// text) for a verdict to count as reviewed / verified; verdicts without it are saved but flagged.</param>
/// <param name="EvidenceDate">Date of the evidence itself (not when it was entered). Must not be in the future.</param>
/// <param name="CorrectionNote">Why an earlier verdict is being replaced; stored in the history.</param>
/// <param name="RecordedBy">Self-reported name (free text; the app has no login).</param>
public sealed record UpsertFeasibilityRequest(
    [Required] string SiteId,
    FeasibilityItemKey ItemKey,
    FeasibilityStatus Status,
    string? Evidence,
    [Url, MaxLength(2048)] string? SourceUrl = null,
    DateOnly? EvidenceDate = null,
    [MaxLength(2000)] string? CorrectionNote = null,
    [MaxLength(200)] string? RecordedBy = null);

/// <summary>Progress rollup for a site's feasibility review (kept separate from the C3 score).</summary>
public sealed record FeasibilitySummaryDto(
    string SiteId,
    int Total,
    int Verified,
    int Concern,
    int Unknown,
    int Reviewed,
    DateTimeOffset? LastReviewedAtUtc);
