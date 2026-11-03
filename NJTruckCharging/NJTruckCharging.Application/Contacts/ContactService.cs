using NJTruckCharging.Application.Abstractions;
using NJTruckCharging.Domain.Entities;

namespace NJTruckCharging.Application.Contacts;

/// <summary>
/// Researched decision-maker contacts for candidate sites. Contact details are only ever entered by a
/// user (optionally with a source URL) — never derived from nearby carriers.
/// </summary>
public sealed class ContactService(IReadStore read, ISiteContactStore store, TimeProvider clock)
{
    public async Task<List<SiteContactDto>> ListAsync(string siteId, CancellationToken ct = default)
    {
        var entities = await read.ToListAsync(read.SiteContacts.Where(c => c.SiteId == siteId), ct);
        return entities
            // Verified first, then unverified (potential), then invalid; within each, decision-making roles
            // (owner, broker, facilities / sustainability / energy manager) before general contacts; newest first.
            .OrderBy(c => c.VerificationStatus == ContactVerificationStatus.Verified ? 0
                        : c.VerificationStatus == ContactVerificationStatus.Unverified ? 1 : 2)
            .ThenBy(c => Sites.ResearchReadiness.IsRelevantRole(c.ContactType) ? 0 : 1)
            .ThenByDescending(c => c.UpdatedAtUtc)
            .Select(Project).ToList();
    }

    public async Task<SiteContactDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var entity = await read.FirstOrDefaultAsync(read.SiteContacts.Where(c => c.Id == id), ct);
        return entity is null ? null : Project(entity);
    }

    public async Task<SiteContactSummaryDto> GetSummaryAsync(string siteId, CancellationToken ct = default)
    {
        var counts = await read.ToListAsync(
            read.SiteContacts.Where(c => c.SiteId == siteId)
                .GroupBy(c => c.VerificationStatus)
                .Select(g => new { g.Key, Count = g.Count() }), ct);

        var verified = counts.Where(c => c.Key == ContactVerificationStatus.Verified).Sum(c => c.Count);
        var unverified = counts.Where(c => c.Key == ContactVerificationStatus.Unverified).Sum(c => c.Count);
        var invalid = counts.Where(c => c.Key == ContactVerificationStatus.Invalid).Sum(c => c.Count);
        var total = verified + unverified + invalid;

        // Nearby carriers are a *separate* signal (near the site, not confirmed on-site).
        var siteKeyId = await read.FirstOrDefaultAsync(read.Sites.Where(s => s.SiteId == siteId).Select(s => (int?)s.Id), ct);
        var nearbyCarriers = siteKeyId is { } cid
            ? await read.CountAsync(read.NearbyFleets.Where(f => f.CandidateSiteId == cid), ct)
            : 0;

        var state = verified > 0 ? SiteContactState.VerifiedDecisionMaker
            : unverified > 0 ? SiteContactState.PotentialUnverified
            : nearbyCarriers > 0 ? SiteContactState.NearbyCarrierOnly
            : SiteContactState.None;

        return new SiteContactSummaryDto(siteId, total, verified, unverified, invalid, nearbyCarriers, state);
    }

    public enum CreateOutcome { Created, CandidateNotFound, SourceRequired }

    /// <summary>A contact can only be marked Verified with a cited source (no unsupported verification claims).</summary>
    public static bool MissingSource(ContactVerificationStatus status, string? sourceUrl) =>
        status == ContactVerificationStatus.Verified && string.IsNullOrWhiteSpace(sourceUrl);

    public async Task<(CreateOutcome Outcome, SiteContactDto? Dto)> CreateAsync(CreateContactRequest req, string? currentUser, CancellationToken ct = default)
    {
        if (MissingSource(req.VerificationStatus ?? ContactVerificationStatus.Unverified, req.SourceUrl))
            return (CreateOutcome.SourceRequired, null);
        var candidateExists = await read.FirstOrDefaultAsync(
            read.Sites.Where(s => s.SiteId == req.SiteId).Select(s => s.SiteId), ct) is not null;
        if (!candidateExists)
            return (CreateOutcome.CandidateNotFound, null);

        var now = clock.GetUtcNow();
        var entity = new SiteContact
        {
            SiteId = req.SiteId,
            Name = req.Name.Trim(),
            Organization = Clean(req.Organization),
            JobTitle = Clean(req.JobTitle),
            ContactType = req.ContactType,
            BusinessPhone = Clean(req.BusinessPhone),
            Email = Clean(req.Email),
            SourceUrl = Clean(req.SourceUrl),
            VerificationStatus = req.VerificationStatus ?? ContactVerificationStatus.Unverified,
            ResearchNotes = Clean(req.ResearchNotes),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            CreatedBy = currentUser,
        };
        if (entity.VerificationStatus == ContactVerificationStatus.Verified)
        {
            entity.LastVerifiedUtc = now;
            entity.VerifiedBy = currentUser;
        }

        await store.AddAsync(entity, ct);
        await store.SaveChangesAsync(ct);
        return (CreateOutcome.Created, await GetByIdAsync(entity.Id, ct));
    }

    public enum UpdateOutcome { Updated, NotFound, SourceRequired }

    public async Task<(UpdateOutcome Outcome, SiteContactDto? Dto)> UpdateAsync(int id, UpdateContactRequest req, string? currentUser, CancellationToken ct = default)
    {
        if (MissingSource(req.VerificationStatus, req.SourceUrl)) return (UpdateOutcome.SourceRequired, null);
        var entity = await store.FindByIdAsync(id, ct);
        if (entity is null) return (UpdateOutcome.NotFound, null);

        entity.Name = req.Name.Trim();
        entity.Organization = Clean(req.Organization);
        entity.JobTitle = Clean(req.JobTitle);
        entity.ContactType = req.ContactType;
        entity.BusinessPhone = Clean(req.BusinessPhone);
        entity.Email = Clean(req.Email);
        entity.SourceUrl = Clean(req.SourceUrl);
        entity.ResearchNotes = Clean(req.ResearchNotes);
        entity.UpdatedAtUtc = clock.GetUtcNow();

        var wasVerified = entity.VerificationStatus == ContactVerificationStatus.Verified;
        entity.VerificationStatus = req.VerificationStatus;
        if (req.VerificationStatus == ContactVerificationStatus.Verified)
        {
            // (Re)confirm the verification stamp whenever it is set/kept verified via an edit.
            entity.LastVerifiedUtc = entity.UpdatedAtUtc;
            entity.VerifiedBy = currentUser;
        }
        else if (wasVerified)
        {
            // Moved away from verified: keep the historical stamp but it no longer counts as verified.
        }

        await store.SaveChangesAsync(ct);
        return (UpdateOutcome.Updated, await GetByIdAsync(id, ct));
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var entity = await store.FindByIdAsync(id, ct);
        if (entity is null) return false;
        store.Remove(entity);
        await store.SaveChangesAsync(ct);
        return true;
    }

    private static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    private static SiteContactDto Project(SiteContact c) => new(
        c.Id, c.SiteId, c.Name, c.Organization, c.JobTitle, c.ContactType,
        c.BusinessPhone, c.Email, c.SourceUrl, c.VerificationStatus, c.LastVerifiedUtc, c.VerifiedBy,
        c.ResearchNotes, c.CreatedAtUtc, c.UpdatedAtUtc, c.CreatedBy);
}
