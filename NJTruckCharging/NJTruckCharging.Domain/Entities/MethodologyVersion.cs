using NJTruckCharging.Domain.Scoring;

namespace NJTruckCharging.Domain.Entities;

public enum MethodologyVersionStatus
{
    /// <summary>Editable; never used for the live score until published.</summary>
    Draft = 0,
    /// <summary>Immutable rules. At most one published version is active (see <see cref="MethodologyState"/>).</summary>
    Published = 1
}

/// <summary>
/// A scoring methodology version: an immutable (once published) rule snapshot plus metadata. Version 1 is the
/// original C3 workbook methodology (<see cref="IsBaseline"/>) and can never be edited or deleted.
/// </summary>
public class MethodologyVersion
{
    public int Id { get; set; }
    public string Name { get; set; } = default!;
    public MethodologyVersionStatus Status { get; set; }
    public bool IsBaseline { get; set; }
    /// <summary>Serialized <c>MethodologyRules</c> (validated schema; never code or SQL).</summary>
    public string RulesJson { get; set; } = default!;
    public string RulesHash { get; set; } = default!;
    public string? ChangeSummary { get; set; }
    public int? BasedOnVersionId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public DateTimeOffset? PublishedAtUtc { get; set; }

    // ---- Preview: proposed scores for all sites, stored in SiteVersionScores --------------------------------
    /// <summary>Rules hash the stored proposed scores were computed with (publish requires it to still match).</summary>
    public string? PreviewRulesHash { get; set; }
    /// <summary><see cref="MethodologyState.DataVersion"/> the stored scores were computed on (a re-import invalidates).</summary>
    public int? ScoresDataVersion { get; set; }
    public DateTimeOffset? PreviewedAtUtc { get; set; }
    /// <summary>Serialized comparison summary shown on the preview screen.</summary>
    public string? PreviewSummaryJson { get; set; }
}

/// <summary>Single row (Id = 1): the active-version pointer. Publishing swaps it inside one transaction.</summary>
public class MethodologyState
{
    public int Id { get; set; } = 1;
    public int? ActiveVersionId { get; set; }
    public int? PreviousActiveVersionId { get; set; }
    public DateTimeOffset? ActivatedAtUtc { get; set; }
    /// <summary>Incremented by every workbook re-import so stale previews cannot be published.</summary>
    public int DataVersion { get; set; } = 1;
    public byte[] RowVersion { get; set; } = default!;
}

public enum ActivationReason { Initial = 0, Publish = 1, Restore = 2, Reimport = 3, Rescore = 4 }

/// <summary>Audit trail of every change of the active methodology (and every re-application to new data).</summary>
public class MethodologyActivation
{
    public int Id { get; set; }
    public int VersionId { get; set; }
    public int? PreviousVersionId { get; set; }
    public ActivationReason Reason { get; set; }
    public DateTimeOffset AtUtc { get; set; }
    public int SitesScored { get; set; }
    public int? DataVersion { get; set; }
    public string? Note { get; set; }
}

/// <summary>
/// Score and rank of one site under one methodology version. Soft-referenced by the stable SiteId (no FK) so
/// re-imports never cascade. The active version's rows are copied onto <see cref="CandidateSite"/> on publish.
/// </summary>
public class SiteVersionScore
{
    public int MethodologyVersionId { get; set; }
    public string SiteId { get; set; } = default!;
    public int Segment { get; set; }
    public double Score { get; set; }
    public int Rank { get; set; }
    public double TruckDemandPoints { get; set; }
    /// <summary>Built-in criteria points (0 when disabled/absent) — materialised onto CandidateSites on publish.</summary>
    public ScoreBreakdown Breakdown { get; set; } = new();
    /// <summary>Compact JSON <c>{"p":[points aligned with the version's criteria], "m":[indexes with missing data]}</c>.</summary>
    public string PointsJson { get; set; } = default!;
}
