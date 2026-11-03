using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using NJTruckCharging.Application.Abstractions;
using NJTruckCharging.Domain.Entities;

namespace NJTruckCharging.Application.Screening;

public sealed record ScreeningEntryDto(ScreeningDecision Decision, string? Evidence, DateTimeOffset RecordedAtUtc);

/// <summary>A site's current screening decision (NotReviewed when none recorded) plus its history, newest first.</summary>
public sealed record SiteScreeningDto(
    string SiteId, ScreeningDecision Decision, string? Evidence, DateTimeOffset? UpdatedAtUtc,
    IReadOnlyList<ScreeningEntryDto> History);

public sealed record UpsertScreeningRequest([Required] string SiteId, ScreeningDecision Decision, string? Evidence);

/// <summary>Screening progress for one NOFA segment (sites without a record are NotReviewed).</summary>
public sealed record SegmentScreeningProgressDto(int Segment, int Total, int NotReviewed, int Promising, int NeedsMoreResearch, int Unsuitable)
{
    public int Reviewed => Total - NotReviewed;
}

/// <summary>
/// Counts per decision overall and per segment (sites without a record are NotReviewed). <see cref="TopN"/>, when
/// set, limits the scope to each segment's top-N ranked sites — the same scope as the screening queue.
/// </summary>
public sealed record ScreeningSummaryDto(int Total, int NotReviewed, int Promising, int NeedsMoreResearch, int Unsuitable)
{
    public int Reviewed => Total - NotReviewed;
    public int? TopN { get; init; }
    public IReadOnlyList<SegmentScreeningProgressDto> Segments { get; init; } = [];
}

/// <summary>Shared screening rules: filter tokens, labels and the SQL predicate used by SiteFilter.</summary>
public static class ScreeningRules
{
    public const int MaxEvidenceLength = 4000;

    /// <summary>Filter tokens: promising | research | unsuitable | notreviewed (enum names also accepted).</summary>
    public static ScreeningDecision[] ParseTokens(IEnumerable<string> tokens) =>
        tokens.Select(t => t.Trim().ToLowerInvariant() switch
            {
                "promising" => (ScreeningDecision?)ScreeningDecision.Promising,
                "research" or "needsmoreresearch" => ScreeningDecision.NeedsMoreResearch,
                "unsuitable" => ScreeningDecision.Unsuitable,
                "notreviewed" or "not-reviewed" => ScreeningDecision.NotReviewed,
                _ => null
            })
            .Where(d => d is not null).Select(d => d!.Value).Distinct().ToArray();

    public static string Label(ScreeningDecision d) => d switch
    {
        ScreeningDecision.Promising => "Promising",
        ScreeningDecision.NeedsMoreResearch => "Needs more research",
        ScreeningDecision.Unsuitable => "Unsuitable",
        _ => "Not reviewed"
    };

    /// <summary>
    /// SQL predicate: the site's screening decision is one of <paramref name="wanted"/>. A site with no record (or
    /// a record reset to NotReviewed) counts as NotReviewed.
    /// </summary>
    public static Expression<Func<CandidateSite, bool>> InDecisions(
        IReadOnlyCollection<ScreeningDecision> wanted, IQueryable<SiteScreening> screenings)
    {
        var decided = wanted.Where(d => d != ScreeningDecision.NotReviewed).ToList();
        var wantNotReviewed = wanted.Contains(ScreeningDecision.NotReviewed);
        return s =>
            screenings.Any(sc => sc.SiteId == s.SiteId && decided.Contains(sc.Decision)) ||
            (wantNotReviewed && !screenings.Any(sc => sc.SiteId == s.SiteId && sc.Decision != ScreeningDecision.NotReviewed));
    }
}

/// <summary>
/// Site screening workflow: record a suitability decision (Promising / Needs more research / Unsuitable / Not
/// reviewed) with evidence. Kept entirely separate from the C3 score; not a CRM pipeline.
/// </summary>
public sealed class ScreeningService(IReadStore read, IScreeningStore store, TimeProvider clock)
{
    public enum UpsertOutcome { Saved, Unchanged, CandidateNotFound, EvidenceRequired, EvidenceTooLong }

    public async Task<SiteScreeningDto> GetAsync(string siteId, CancellationToken ct = default)
    {
        var current = await read.FirstOrDefaultAsync(read.SiteScreenings.Where(x => x.SiteId == siteId), ct);
        var history = await read.ToListAsync(read.SiteScreeningEntries
            .Where(x => x.SiteId == siteId)
            .OrderByDescending(x => x.RecordedAtUtc).ThenByDescending(x => x.Id)
            .Select(x => new ScreeningEntryDto(x.Decision, x.Evidence, x.RecordedAtUtc)), ct);
        return current is null
            ? new SiteScreeningDto(siteId, ScreeningDecision.NotReviewed, null, null, history)
            : new SiteScreeningDto(siteId, current.Decision, current.Evidence, current.UpdatedAtUtc, history);
    }

    /// <summary>
    /// Sets the decision. Any decision other than NotReviewed must carry evidence (what was seen and where).
    /// Each real change is appended to the history.
    /// </summary>
    public async Task<(UpsertOutcome Outcome, SiteScreeningDto? Dto)> UpsertAsync(UpsertScreeningRequest req, CancellationToken ct = default)
    {
        var evidence = string.IsNullOrWhiteSpace(req.Evidence) ? null : req.Evidence.Trim();
        if (req.Decision != ScreeningDecision.NotReviewed && evidence is null)
            return (UpsertOutcome.EvidenceRequired, null);
        if (evidence is { Length: > ScreeningRules.MaxEvidenceLength })
            return (UpsertOutcome.EvidenceTooLong, null);

        var exists = await read.FirstOrDefaultAsync(read.Sites.Where(s => s.SiteId == req.SiteId).Select(s => s.SiteId), ct) is not null;
        if (!exists) return (UpsertOutcome.CandidateNotFound, null);

        var now = clock.GetUtcNow();
        var entity = await store.FindAsync(req.SiteId, ct);
        if (entity is not null && entity.Decision == req.Decision && entity.Evidence == evidence)
            return (UpsertOutcome.Unchanged, await GetAsync(req.SiteId, ct));
        if (entity is null)
        {
            if (req.Decision == ScreeningDecision.NotReviewed && evidence is null)
                return (UpsertOutcome.Unchanged, await GetAsync(req.SiteId, ct));
            entity = new SiteScreening { SiteId = req.SiteId, CreatedAtUtc = now };
            await store.AddAsync(entity, ct);
        }

        entity.Decision = req.Decision;
        entity.Evidence = evidence;
        entity.UpdatedAtUtc = now;
        await store.AddEntryAsync(new SiteScreeningEntry { SiteId = req.SiteId, Decision = req.Decision, Evidence = evidence, RecordedAtUtc = now }, ct);
        await store.SaveChangesAsync(ct);
        return (UpsertOutcome.Saved, await GetAsync(req.SiteId, ct));
    }

    public async Task<ScreeningSummaryDto> GetSummaryAsync(int? topN = null, CancellationToken ct = default)
    {
        var sites = topN is > 0 ? read.Sites.Where(s => s.ComputedRank <= topN) : read.Sites;
        var totals = (await read.ToListAsync(sites.GroupBy(s => s.Segment)
                .Select(g => new { Segment = g.Key, Count = g.Count() }), ct))
            .ToDictionary(x => x.Segment, x => x.Count);
        // Only decisions whose SiteId is in scope (and still in the dataset) count.
        var counts = await read.ToListAsync(
            from sc in read.SiteScreenings
            join s in sites on sc.SiteId equals s.SiteId
            group sc by new { s.Segment, sc.Decision } into g
            select new { g.Key.Segment, g.Key.Decision, Count = g.Count() }, ct);

        var segments = totals.Keys.Order().Select(seg =>
        {
            int C(ScreeningDecision d) => counts.Where(c => c.Segment == seg && c.Decision == d).Sum(c => c.Count);
            var decided = C(ScreeningDecision.Promising) + C(ScreeningDecision.NeedsMoreResearch) + C(ScreeningDecision.Unsuitable);
            return new SegmentScreeningProgressDto(seg, totals[seg], totals[seg] - decided,
                C(ScreeningDecision.Promising), C(ScreeningDecision.NeedsMoreResearch), C(ScreeningDecision.Unsuitable));
        }).ToList();

        return new ScreeningSummaryDto(segments.Sum(s => s.Total), segments.Sum(s => s.NotReviewed),
            segments.Sum(s => s.Promising), segments.Sum(s => s.NeedsMoreResearch), segments.Sum(s => s.Unsuitable))
        {
            TopN = topN is > 0 ? topN : null,
            Segments = segments
        };
    }
}
