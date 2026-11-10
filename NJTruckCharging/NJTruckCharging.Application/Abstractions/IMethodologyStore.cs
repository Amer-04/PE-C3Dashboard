using NJTruckCharging.Domain.Entities;

namespace NJTruckCharging.Application.Abstractions;

public enum ActivationFailure
{
    None,
    NotFound,
    /// <summary>Another publish / preview / import holds the scoring lock.</summary>
    ConcurrentOperation,
    /// <summary>The active version is not the one the caller previewed against.</summary>
    ActiveVersionChanged,
    /// <summary>Rules or data changed since the preview (re-run the preview).</summary>
    PreviewStale,
    /// <summary>Stored proposed scores do not cover exactly the current candidate sites.</summary>
    ScoresIncomplete,
    /// <summary>Unexpected error — the transaction was rolled back.</summary>
    Failed
}

public sealed record ActivateCommand(
    int VersionId, int? ExpectedActiveVersionId, string ExpectedRulesHash,
    ActivationReason Reason, DateTimeOffset Now, string? Note);

public sealed record ActivateResult(ActivationFailure Failure, string? Message, int SitesUpdated, int? PreviousVersionId)
{
    public bool Ok => Failure == ActivationFailure.None;
}

/// <summary>A version's freshly computed scores, written together with the import that produced the data.</summary>
public sealed record VersionScoreSet(int VersionId, IReadOnlyList<SiteVersionScore> Rows);

/// <summary>Scoring results to persist atomically with a workbook re-import (active methodology re-applied).</summary>
public sealed record ReimportScoring(int? ActiveVersionId, IReadOnlyList<VersionScoreSet> ScoreSets, DateTimeOffset Now);

/// <summary>Persistence of methodology versions, per-version scores and the active-version pointer.</summary>
public interface IMethodologyStore
{
    Task<MethodologyState?> GetStateAsync(CancellationToken ct = default);
    /// <summary>Tracked entity (changes saved by <see cref="SaveChangesAsync"/>).</summary>
    Task<MethodologyVersion?> FindVersionAsync(int id, CancellationToken ct = default);
    Task<MethodologyVersion?> FindBaselineAsync(CancellationToken ct = default);
    Task AddVersionAsync(MethodologyVersion version, CancellationToken ct = default);
    Task DeleteVersionAsync(MethodologyVersion version, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);

    /// <summary>Creates the singleton state row if missing.</summary>
    Task EnsureStateAsync(CancellationToken ct = default);

    /// <summary>All candidate sites, untracked, in a deterministic order (segment, SiteId).</summary>
    Task<List<CandidateSite>> LoadSitesForScoringAsync(CancellationToken ct = default);

    /// <summary>
    /// Replaces a version's stored scores and records the preview metadata, in one transaction under the scoring
    /// lock. Never touches the live score columns.
    /// </summary>
    Task<ActivationFailure> StoreScoresAsync(int versionId, IReadOnlyList<SiteVersionScore> rows, string rulesHash,
        int dataVersion, string? summaryJson, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>
    /// Atomically makes a previewed version active: verifies the pointer, preview hash, data version and score
    /// completeness, copies the version's scores onto every CandidateSite row and moves the pointer — all in one
    /// transaction. On any failure nothing changes.
    /// </summary>
    Task<ActivateResult> ActivateAsync(ActivateCommand command, CancellationToken ct = default);
}

/// <summary>Test seam: runs inside the activation transaction just before commit (default: no-op).</summary>
public interface IActivationHook
{
    Task BeforeCommitAsync(int versionId, CancellationToken ct);
}

public sealed class NoOpActivationHook : IActivationHook
{
    public Task BeforeCommitAsync(int versionId, CancellationToken ct) => Task.CompletedTask;
}
