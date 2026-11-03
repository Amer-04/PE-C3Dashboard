using NJTruckCharging.Application.Abstractions;
using NJTruckCharging.Domain.Entities;

namespace NJTruckCharging.Application.SavedSites;

/// <summary>
/// Bookmarking candidate sites for closer evaluation. Bookmarks are keyed by the stable
/// <see cref="CandidateSite.SiteId"/> so they survive workbook re-imports. This is not a CRM:
/// there is no pipeline, assignment, or follow-up — just a bookmark and an optional note.
/// </summary>
public sealed class SavedSiteService(IReadStore read, ISavedSiteStore store, TimeProvider clock)
{
    public Task<List<SavedSiteDto>> ListAsync(CancellationToken ct = default) =>
        read.ToListAsync(Project(read.SavedSites).OrderByDescending(d => d.UpdatedAtUtc), ct);

    public Task<SavedSiteDto?> GetByIdAsync(int id, CancellationToken ct = default) =>
        read.FirstOrDefaultAsync(Project(read.SavedSites.Where(x => x.Id == id)), ct);

    public Task<SavedSiteDto?> GetBySiteIdAsync(string siteId, CancellationToken ct = default) =>
        read.FirstOrDefaultAsync(Project(read.SavedSites.Where(x => x.SiteId == siteId)), ct);

    /// <summary>The SiteIds currently bookmarked — lets the dashboard mark saved sites cheaply.</summary>
    public Task<List<SavedSiteRef>> ListRefsAsync(CancellationToken ct = default) =>
        read.ToListAsync(read.SavedSites.Select(x => new SavedSiteRef(x.Id, x.SiteId)), ct);

    public enum SaveOutcome { Created, AlreadySaved, CandidateNotFound }

    /// <summary>Bookmarks a candidate. Idempotent: bookmarking an already-saved SiteId returns the existing row.</summary>
    public async Task<(SaveOutcome Outcome, SavedSiteDto? Dto)> SaveAsync(SaveSiteRequest req, CancellationToken ct = default)
    {
        var candidateExists = await read.FirstOrDefaultAsync(
            read.Sites.Where(c => c.SiteId == req.SiteId).Select(c => c.SiteId), ct) is not null;
        if (!candidateExists)
            return (SaveOutcome.CandidateNotFound, null);

        var existing = await store.FindBySiteIdAsync(req.SiteId, ct);
        if (existing is not null)
            return (SaveOutcome.AlreadySaved, await GetByIdAsync(existing.Id, ct));

        var now = clock.GetUtcNow();
        var entity = new SavedSite
        {
            SiteId = req.SiteId,
            Notes = req.Notes,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        await store.AddAsync(entity, ct);
        await store.SaveChangesAsync(ct);
        return (SaveOutcome.Created, await GetByIdAsync(entity.Id, ct));
    }

    public async Task<SavedSiteDto?> UpdateAsync(int id, UpdateSavedSiteRequest req, CancellationToken ct = default)
    {
        var entity = await store.FindByIdAsync(id, ct);
        if (entity is null) return null;

        entity.Notes = req.Notes;
        entity.UpdatedAtUtc = clock.GetUtcNow();

        await store.SaveChangesAsync(ct);
        return await GetByIdAsync(id, ct);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var entity = await store.FindByIdAsync(id, ct);
        if (entity is null) return false;
        store.Remove(entity);
        await store.SaveChangesAsync(ct);
        return true;
    }

    // Left join bookmarks to candidates by the stable SiteId. c is null when the candidate is gone.
    private IQueryable<SavedSiteDto> Project(IQueryable<SavedSite> saved) =>
        from sv in saved
        join cand in read.Sites on sv.SiteId equals cand.SiteId into g
        from c in g.DefaultIfEmpty()
        select new SavedSiteDto
        {
            Id = sv.Id,
            SiteId = sv.SiteId,
            Notes = sv.Notes,
            CreatedAtUtc = sv.CreatedAtUtc,
            UpdatedAtUtc = sv.UpdatedAtUtc,
            CandidateFound = c != null,
            CandidateId = c != null ? c.Id : (int?)null,
            Segment = c != null ? c.Segment : (int?)null,
            Rank = c != null ? c.ComputedRank : (int?)null,
            Score = c != null ? c.Score : (double?)null,
            Address = c != null ? c.SiteAddress : null,
            Municipality = c != null ? c.Municipality : null,
            County = c != null ? c.County : null,
            PropClass = c != null ? c.PropClass : null,
            OpenAcres = c != null ? c.OpenAcresEst : (double?)null,
            MilesToExit = c != null ? c.MilesToExit : (double?)null,
            NearestHighway = c != null ? c.NearestHighway : null,
            OwnerName = c != null ? c.OwnerName : null,
            OwnerMailAddress = c != null ? c.OwnerMailAddress : null,
            OwnerMailCity = c != null ? c.OwnerMailCity : null,
            OwnerMailZip = c != null ? c.OwnerMailZip : null,
            BusinessPhone = c != null ? c.BusinessPhone : null,
            Lat = c != null ? c.Latitude : (double?)null,
            Lng = c != null ? c.Longitude : (double?)null
        };
}
