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

    /// <summary>
    /// Optimistic-concurrency counter for draft edits: every save sends the revision it was based on and increments it.
    /// Also an EF concurrency token, so two saves racing on the same row cannot both succeed.
    /// </summary>
    public int Revision { get; set; }

    // ---- Audit: who created, last edited and first published this version (Supabase user id + verified e-mail) ----
    public string? CreatedByUserId { get; set; }
    public string? CreatedByEmail { get; set; }
    public string? UpdatedByUserId { get; set; }
    public string? UpdatedByEmail { get; set; }
    public string? PublishedByUserId { get; set; }
    public string? PublishedByEmail { get; set; }

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
    /// <summary>Who switched live scoring (Supabase user id + verified e-mail); null for system re-applications.</summary>
    public string? ActorUserId { get; set; }
    public string? ActorEmail { get; set; }
}

public enum MethodologyChangeAction { CreateDraft = 0, SaveDraft = 1, DeleteDraft = 2, Preview = 3, Publish = 4, Restore = 5 }

/// <summary>
/// Append-only audit of every methodology change made through the API: who (Supabase user id + the e-mail Supabase
/// verified), when, which version, the rules fingerprint before and after, the criterion-level differences and, for
/// saves and deletions, full before/after snapshots. No foreign key: entries outlive deleted drafts.
/// </summary>
public class MethodologyChange
{
    public long Id { get; set; }
    public int VersionId { get; set; }
    public string VersionName { get; set; } = default!;
    public MethodologyChangeAction Action { get; set; }
    public DateTimeOffset AtUtc { get; set; }
    public string? ActorUserId { get; set; }
    public string? ActorEmail { get; set; }
    public int? RevisionBefore { get; set; }
    public int? RevisionAfter { get; set; }
    public string? RulesHashBefore { get; set; }
    public string? RulesHashAfter { get; set; }
    /// <summary>For Publish/Restore: the version that was active before.</summary>
    public int? PreviousActiveVersionId { get; set; }
    /// <summary>Serialized list of criterion-level differences (previous → new values).</summary>
    public string? ChangesJson { get; set; }
    /// <summary>Full previous state (name, summary, rules): the source version (create), the draft before a save or
    /// deletion, or the previously live version (publish/restore). Null only for Preview, which changes no rules.</summary>
    public string? BeforeJson { get; set; }
    /// <summary>Full new state (name, summary, rules) for create, save and publish/restore; null for deletions and Preview.</summary>
    public string? AfterJson { get; set; }
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
