using NJTruckCharging.Application.Abstractions;
using NJTruckCharging.Application.Research;
using NJTruckCharging.Domain.Entities;

namespace NJTruckCharging.Application.Feasibility;

/// <summary>
/// The site-feasibility checklist (truck access, maneuvering, usable land, parking, highway access,
/// electrical, flood/environmental, zoning, ownership/lease, owner interest). A ground/records
/// verification kept entirely separate from the computed C3 score. Every change is appended to the research
/// history in the same save, so an overwritten verdict is never lost.
/// </summary>
public sealed class FeasibilityService(IReadStore read, IFeasibilityStore store, IResearchHistoryStore history, TimeProvider clock)
{
    private static readonly FeasibilityItemKey[] AllKeys = Enum.GetValues<FeasibilityItemKey>();

    /// <summary>All checklist items for a site, stored verdicts merged over Unknown defaults, in canonical order.</summary>
    public async Task<List<FeasibilityItemDto>> GetChecklistAsync(string siteId, CancellationToken ct = default)
    {
        var stored = (await read.ToListAsync(read.FeasibilityItems.Where(x => x.SiteId == siteId), ct))
            .ToDictionary(x => x.ItemKey);

        return AllKeys.Select(key => stored.TryGetValue(key, out var e)
            ? Project(e)
            : new FeasibilityItemDto(key, FeasibilityStatus.Unknown, null, null, null, null)).ToList();
    }

    public async Task<FeasibilitySummaryDto> GetSummaryAsync(string siteId, CancellationToken ct = default)
    {
        var items = await read.ToListAsync(read.FeasibilityItems.Where(x => x.SiteId == siteId), ct);
        var verified = items.Count(i => i.Status == FeasibilityStatus.Verified);
        var concern = items.Count(i => i.Status == FeasibilityStatus.Concern);
        var reviewed = verified + concern;
        var lastReviewed = items.Where(i => i.ReviewedAtUtc != null).Select(i => i.ReviewedAtUtc).DefaultIfEmpty(null).Max();
        return new FeasibilitySummaryDto(siteId, AllKeys.Length, verified, concern, AllKeys.Length - reviewed, reviewed, lastReviewed);
    }

    public enum UpsertOutcome { Saved, CandidateNotFound, EvidenceRequired, InvalidEvidenceDate, RecordedByRequired }

    /// <summary>Earliest plausible evidence date (rejects typos such as year 0202).</summary>
    public static readonly DateOnly MinEvidenceDate = new(1900, 1, 1);

    /// <summary>Create or update one checklist item; records the verdict, its evidence and an append-only history entry.</summary>
    public async Task<(UpsertOutcome Outcome, FeasibilityItemDto? Dto)> UpsertAsync(UpsertFeasibilityRequest req, string? currentUser, CancellationToken ct = default)
    {
        // Manual IOS confirmation is the only path to IOS "Confirmed", so it must carry evidence. Parcel and
        // business-at-address verdicts drive the Site Evidence column (Verified / Mismatch), so they need evidence too.
        // A Conflicting verdict must say what disagrees with what.
        if (string.IsNullOrWhiteSpace(req.Evidence) && (
                (req.ItemKey == FeasibilityItemKey.OutdoorStorageUse && req.Status == FeasibilityStatus.Verified) ||
                ((req.ItemKey is FeasibilityItemKey.ParcelIdentity or FeasibilityItemKey.BusinessAtAddress) && req.Status != FeasibilityStatus.Unknown) ||
                req.Status == FeasibilityStatus.Conflicting))
            return (UpsertOutcome.EvidenceRequired, null);

        var now = clock.GetUtcNow();
        if (req.EvidenceDate is { } d && (d > DateOnly.FromDateTime(now.UtcDateTime).AddDays(1) || d < MinEvidenceDate))
            return (UpsertOutcome.InvalidEvidenceDate, null);

        var candidateExists = await read.FirstOrDefaultAsync(
            read.Sites.Where(s => s.SiteId == req.SiteId).Select(s => s.SiteId), ct) is not null;
        if (!candidateExists)
            return (UpsertOutcome.CandidateNotFound, null);

        var evidence = Clean(req.Evidence);
        var source = Clean(req.SourceUrl);
        var recordedBy = Clean(req.RecordedBy) ?? currentUser;
        // A new Verified verdict must name who recorded it (self-reported free text; the app has no login).
        if (req.Status == FeasibilityStatus.Verified && recordedBy is null)
            return (UpsertOutcome.RecordedByRequired, null);

        var entity = await store.FindAsync(req.SiteId, req.ItemKey, ct);
        var previous = entity?.Status ?? FeasibilityStatus.Unknown;
        if (entity is not null && entity.Status == req.Status && entity.Evidence == evidence
            && entity.SourceUrl == source && entity.EvidenceDate == req.EvidenceDate && Clean(req.CorrectionNote) is null)
            return (UpsertOutcome.Saved, Project(entity));   // nothing changed — no duplicate history entry

        if (entity is null)
        {
            entity = new FeasibilityItem { SiteId = req.SiteId, ItemKey = req.ItemKey, CreatedAtUtc = now };
            await store.AddAsync(entity, ct);
        }

        entity.Status = req.Status;
        entity.Evidence = evidence;
        entity.SourceUrl = source;
        entity.EvidenceDate = req.EvidenceDate;
        entity.Reviewer = recordedBy;
        entity.ReviewedAtUtc = now;
        entity.UpdatedAtUtc = now;

        await history.AppendAsync(new ResearchHistoryEntry
        {
            SiteId = req.SiteId,
            Subject = ResearchSubject.Feasibility,
            ItemKey = req.ItemKey,
            // Replacing an earlier verdict with a different one is a correction; the old one stays in the history.
            Action = previous != FeasibilityStatus.Unknown && previous != req.Status ? ResearchAction.Corrected : ResearchAction.Recorded,
            PreviousStatus = previous.ToString(),
            Status = req.Status.ToString(),
            Evidence = evidence,
            SourceUrl = source,
            EvidenceDate = req.EvidenceDate,
            Note = Clean(req.CorrectionNote),
            RecordedBy = recordedBy,
            RecordedAtUtc = now
        }, ct);

        await store.SaveChangesAsync(ct);   // one unit of work: the verdict and its history entry commit together
        return (UpsertOutcome.Saved, Project(entity));
    }

    private static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    private static FeasibilityItemDto Project(FeasibilityItem e) =>
        new(e.ItemKey, e.Status, e.Evidence, e.Reviewer, e.ReviewedAtUtc, e.UpdatedAtUtc)
        {
            SourceUrl = e.SourceUrl,
            EvidenceDate = e.EvidenceDate,
            EvidenceState = ResearchQuality.State(e)
        };
}
