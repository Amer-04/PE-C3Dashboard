using NJTruckCharging.Application.Abstractions;
using NJTruckCharging.Domain.Entities;

namespace NJTruckCharging.Application.Feasibility;

/// <summary>
/// The site-feasibility checklist (truck access, maneuvering, usable land, parking, highway access,
/// electrical, flood/environmental, zoning, ownership/lease, owner interest). A ground/records
/// verification kept entirely separate from the computed C3 score.
/// </summary>
public sealed class FeasibilityService(IReadStore read, IFeasibilityStore store, TimeProvider clock)
{
    private static readonly FeasibilityItemKey[] AllKeys = Enum.GetValues<FeasibilityItemKey>();

    /// <summary>All checklist items for a site, stored verdicts merged over Unknown defaults, in canonical order.</summary>
    public async Task<List<FeasibilityItemDto>> GetChecklistAsync(string siteId, CancellationToken ct = default)
    {
        var stored = (await read.ToListAsync(read.FeasibilityItems.Where(x => x.SiteId == siteId), ct))
            .ToDictionary(x => x.ItemKey);

        return AllKeys.Select(key => stored.TryGetValue(key, out var e)
            ? new FeasibilityItemDto(key, e.Status, e.Evidence, e.Reviewer, e.ReviewedAtUtc, e.UpdatedAtUtc)
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

    public enum UpsertOutcome { Saved, CandidateNotFound, EvidenceRequired }

    /// <summary>Create or update one checklist item; records the reviewer and time.</summary>
    public async Task<(UpsertOutcome Outcome, FeasibilityItemDto? Dto)> UpsertAsync(UpsertFeasibilityRequest req, string? currentUser, CancellationToken ct = default)
    {
        // Manual IOS confirmation is the only path to IOS "Confirmed", so it must carry evidence. Parcel and
        // business-at-address verdicts drive the Site Evidence column (Verified / Mismatch), so they need evidence too.
        if (string.IsNullOrWhiteSpace(req.Evidence) && (
                (req.ItemKey == FeasibilityItemKey.OutdoorStorageUse && req.Status == FeasibilityStatus.Verified) ||
                ((req.ItemKey is FeasibilityItemKey.ParcelIdentity or FeasibilityItemKey.BusinessAtAddress) && req.Status != FeasibilityStatus.Unknown)))
            return (UpsertOutcome.EvidenceRequired, null);

        var candidateExists = await read.FirstOrDefaultAsync(
            read.Sites.Where(s => s.SiteId == req.SiteId).Select(s => s.SiteId), ct) is not null;
        if (!candidateExists)
            return (UpsertOutcome.CandidateNotFound, null);

        var now = clock.GetUtcNow();
        var entity = await store.FindAsync(req.SiteId, req.ItemKey, ct);
        if (entity is null)
        {
            entity = new FeasibilityItem { SiteId = req.SiteId, ItemKey = req.ItemKey, CreatedAtUtc = now };
            await store.AddAsync(entity, ct);
        }

        entity.Status = req.Status;
        entity.Evidence = string.IsNullOrWhiteSpace(req.Evidence) ? null : req.Evidence.Trim();
        entity.Reviewer = currentUser;
        entity.ReviewedAtUtc = now;
        entity.UpdatedAtUtc = now;

        await store.SaveChangesAsync(ct);
        return (UpsertOutcome.Saved,
            new FeasibilityItemDto(entity.ItemKey, entity.Status, entity.Evidence, entity.Reviewer, entity.ReviewedAtUtc, entity.UpdatedAtUtc));
    }
}
