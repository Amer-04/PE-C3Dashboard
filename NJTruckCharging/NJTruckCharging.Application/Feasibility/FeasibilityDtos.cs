using System.ComponentModel.DataAnnotations;
using NJTruckCharging.Domain.Entities;

namespace NJTruckCharging.Application.Feasibility;

/// <summary>One checklist line (always present for all 10 items, defaulting to Unknown).</summary>
public sealed record FeasibilityItemDto(
    FeasibilityItemKey ItemKey,
    FeasibilityStatus Status,
    string? Evidence,
    string? Reviewer,
    DateTimeOffset? ReviewedAtUtc,
    DateTimeOffset? UpdatedAtUtc);

public sealed record UpsertFeasibilityRequest(
    [Required] string SiteId,
    FeasibilityItemKey ItemKey,
    FeasibilityStatus Status,
    string? Evidence);

/// <summary>Progress rollup for a site's feasibility review (kept separate from the C3 score).</summary>
public sealed record FeasibilitySummaryDto(
    string SiteId,
    int Total,
    int Verified,
    int Concern,
    int Unknown,
    int Reviewed,
    DateTimeOffset? LastReviewedAtUtc);
