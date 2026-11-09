using NJTruckCharging.Application.Abstractions;
using NJTruckCharging.Domain.Entities;

namespace NJTruckCharging.Application.Research;

/// <summary>
/// Research quality: append-only history, "Start research", per-site completeness, the review queue and
/// per-segment progress. Reads research records only; never reads or writes the C3 score, rank or methodology.
/// Not a CRM: no owners, assignments, stages or outreach.
/// </summary>
public sealed class ResearchService(IReadStore read, IResearchHistoryStore history, TimeProvider clock)
{
    private ResearchSources Sources => ResearchSources.From(read);

    public Task<List<ResearchHistoryEntryDto>> GetHistoryAsync(string siteId, CancellationToken ct = default) =>
        read.ToListAsync(read.ResearchHistory.Where(h => h.SiteId == siteId)
            .OrderByDescending(h => h.RecordedAtUtc).ThenByDescending(h => h.Id)
            .Select(h => new ResearchHistoryEntryDto(h.Id, h.SiteId, h.Subject, h.Action, h.ItemKey, h.ContactId,
                h.PreviousStatus, h.Status, h.Evidence, h.SourceUrl, h.EvidenceDate, h.Note, h.RecordedBy, h.RecordedAtUtc)), ct);

    public enum StartOutcome { Started, AlreadyInResearch, CandidateNotFound }

    /// <summary>Adds a candidate to the research workflow by recording a "Started" history entry (idempotent).</summary>
    public async Task<(StartOutcome Outcome, SiteResearchDto? Dto)> StartAsync(StartResearchRequest req, CancellationToken ct = default)
    {
        var exists = await read.FirstOrDefaultAsync(read.Sites.Where(s => s.SiteId == req.SiteId).Select(s => s.SiteId), ct) is not null;
        if (!exists) return (StartOutcome.CandidateNotFound, null);
        if (await IsInResearchSetAsync(req.SiteId, ct))
            return (StartOutcome.AlreadyInResearch, await GetSiteAsync(req.SiteId, ct));

        await history.AppendAsync(new ResearchHistoryEntry
        {
            SiteId = req.SiteId,
            Subject = ResearchSubject.Site,
            Action = ResearchAction.Started,
            Note = Clean(req.Note),
            RecordedBy = Clean(req.RecordedBy),
            RecordedAtUtc = clock.GetUtcNow()
        }, ct);
        await history.SaveChangesAsync(ct);
        return (StartOutcome.Started, await GetSiteAsync(req.SiteId, ct));
    }

    public async Task<bool> IsInResearchSetAsync(string siteId, CancellationToken ct = default) =>
        await read.CountAsync(read.Sites.Where(s => s.SiteId == siteId).Where(ResearchQuality.InResearchSet(Sources)), ct) > 0;

    /// <summary>Completeness, check states and open issues for one site (null when the SiteId is unknown).</summary>
    public async Task<SiteResearchDto?> GetSiteAsync(string siteId, CancellationToken ct = default)
    {
        var exists = await read.FirstOrDefaultAsync(read.Sites.Where(s => s.SiteId == siteId).Select(s => s.SiteId), ct) is not null;
        if (!exists) return null;
        var feas = await read.ToListAsync(read.FeasibilityItems.Where(f => f.SiteId == siteId), ct);
        var contacts = await read.ToListAsync(read.SiteContacts.Where(c => c.SiteId == siteId), ct);
        var decision = await read.FirstOrDefaultAsync(read.SiteScreenings.Where(x => x.SiteId == siteId).Select(x => (ScreeningDecision?)x.Decision), ct);
        return Build(siteId, await IsInResearchSetAsync(siteId, ct), feas, contacts, decision == ScreeningDecision.Unsuitable);
    }

    public async Task<ReviewQueueDto> GetQueueAsync(int[]? segments, string[]? issueTokens, CancellationToken ct = default)
    {
        var rows = await LoadResearchSetAsync(segments, ct);
        var wanted = issueTokens is { Length: > 0 } ? ResearchQuality.ParseIssues(issueTokens) : [];
        var counts = Enum.GetValues<ReviewIssue>().ToDictionary(i => i, _ => 0);
        var items = new List<ReviewQueueItemDto>();
        var excluded = 0;
        foreach (var r in rows)
        {
            // Screening a site Unsuitable (which requires evidence) is a documented resolution: it leaves the queue.
            if (r.Research.ScreenedUnsuitable) { excluded++; continue; }
            if (r.Research.Issues.Count == 0) continue;
            foreach (var i in r.Research.Issues) counts[i.Issue]++;
            if (wanted.Length > 0 && !r.Research.Issues.Any(i => wanted.Contains(i.Issue))) continue;
            items.Add(new ReviewQueueItemDto(r.Id, r.SiteId, r.Segment, r.Rank, r.Address, r.Municipality, r.County,
                r.Research.Reviewed, r.Research.Verified, r.Research.Total, r.Research.Issues));
        }
        var ordered = items.OrderByDescending(i => i.Issues.Count).ThenBy(i => i.Segment).ThenBy(i => i.Rank).ToList();
        return new ReviewQueueDto(rows.Count, rows.Count(r => !r.Research.ScreenedUnsuitable && r.Research.Issues.Count > 0),
            excluded, counts, ordered);
    }

    public async Task<ResearchSummaryDto> GetSummaryAsync(int[]? segments, CancellationToken ct = default)
    {
        var rows = await LoadResearchSetAsync(segments, ct);
        var segs = rows.GroupBy(r => r.Segment).OrderBy(g => g.Key).Select(g => new SegmentResearchDto(
            g.Key, g.Count(),
            g.Count(r => r.Research.Stage == ResearchStage.NoneReviewed),
            g.Count(r => r.Research.Stage == ResearchStage.PartlyReviewed),
            g.Count(r => r.Research.Stage == ResearchStage.AllReviewed),
            g.Sum(r => r.Research.Reviewed), g.Sum(r => r.Research.Verified), g.Count() * ResearchQuality.CheckCount,
            g.Count(r => !r.Research.ScreenedUnsuitable && r.Research.Issues.Count > 0))).ToList();
        return new ResearchSummaryDto(segs.Sum(s => s.ResearchSites), segs.Sum(s => s.NoneReviewed), segs.Sum(s => s.PartlyReviewed),
            segs.Sum(s => s.AllReviewed), segs.Sum(s => s.ChecksReviewed), segs.Sum(s => s.ChecksVerified), segs.Sum(s => s.ChecksTotal),
            segs.Sum(s => s.SitesWithIssues), segs);
    }

    /// <summary>Per-site reviewed/verified counts for list rows (no research set check; zero for untouched sites).</summary>
    public async Task<Dictionary<string, (int Reviewed, int Verified)>> CountsAsync(IReadOnlyCollection<string> siteIds, CancellationToken ct = default)
    {
        if (siteIds.Count == 0) return [];
        var ids = siteIds.ToList();
        var feas = (await read.ToListAsync(read.FeasibilityItems.Where(f => ids.Contains(f.SiteId)), ct)).ToLookup(f => f.SiteId);
        var contacts = (await read.ToListAsync(read.SiteContacts.Where(c => ids.Contains(c.SiteId)), ct)).ToLookup(c => c.SiteId);
        return ids.Distinct().ToDictionary(id => id, id => ResearchQuality.Counts(ResearchQuality.States(feas[id], contacts[id])));
    }

    private sealed record Row(int Id, string SiteId, int Segment, int Rank, string Address, string Municipality, string County, SiteResearchDto Research);

    private async Task<List<Row>> LoadResearchSetAsync(int[]? segments, CancellationToken ct)
    {
        var q = read.Sites.Where(ResearchQuality.InResearchSet(Sources));
        if (segments is { Length: > 0 }) q = q.Where(s => segments.Contains(s.Segment));
        var sites = await read.ToListAsync(q.Select(s => new { s.Id, s.SiteId, s.Segment, s.ComputedRank, s.SiteAddress, s.Municipality, s.County }), ct);
        if (sites.Count == 0) return [];
        var ids = sites.Select(s => s.SiteId).ToList();
        var feas = (await read.ToListAsync(read.FeasibilityItems.Where(f => ids.Contains(f.SiteId)), ct)).ToLookup(f => f.SiteId);
        var contacts = (await read.ToListAsync(read.SiteContacts.Where(c => ids.Contains(c.SiteId)), ct)).ToLookup(c => c.SiteId);
        var unsuitable = (await read.ToListAsync(read.SiteScreenings
            .Where(x => ids.Contains(x.SiteId) && x.Decision == ScreeningDecision.Unsuitable).Select(x => x.SiteId), ct)).ToHashSet();
        return sites.Select(s => new Row(s.Id, s.SiteId, s.Segment, s.ComputedRank, s.SiteAddress, s.Municipality, s.County,
            Build(s.SiteId, true, feas[s.SiteId], contacts[s.SiteId], unsuitable.Contains(s.SiteId)))).ToList();
    }

    private static SiteResearchDto Build(string siteId, bool inSet, IEnumerable<FeasibilityItem> feas, IEnumerable<SiteContact> contacts, bool unsuitable)
    {
        var feasList = feas.ToList();
        var contactList = contacts.ToList();
        var states = ResearchQuality.States(feasList, contactList);
        var (reviewed, verified) = ResearchQuality.Counts(states);
        var checks = ResearchQuality.AllChecks.Select(c => new ResearchCheckDto(c, ResearchQuality.Label(c), states[c],
            ResearchQuality.CheckItems.TryGetValue(c, out var k) ? k : null)).ToList();
        var issues = ResearchQuality.Issues(feasList, contactList).Select(i => new ReviewIssueDto(i, ResearchQuality.Label(i))).ToList();
        return new SiteResearchDto(siteId, inSet, reviewed, verified, ResearchQuality.CheckCount, ResearchQuality.Stage(reviewed),
            checks, issues, unsuitable);
    }

    private static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
}
