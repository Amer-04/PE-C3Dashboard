using NJTruckCharging.Application.Abstractions;
using NJTruckCharging.Application.Sites;

namespace NJTruckCharging.Application.Dashboard;

public sealed class DashboardService(IReadStore store, TimeProvider clock)
{
    /// <summary>NJ C3 MHD EV charging NOFA – proposals due Dec 23, 2026.</summary>
    public static readonly DateOnly ProposalDeadline = new(2026, 12, 23);

    public async Task<DashboardSummaryDto> GetSummaryAsync(SiteFilter filter, CancellationToken ct = default)
    {
        var total = await store.CountAsync(store.Sites, ct);
        var rows = await store.ToListAsync(filter.Apply(store.Sites, store.FeasibilityItems, store.SiteContacts, store.SiteScreenings).Select(s => new
        {
            s.Id, s.Segment, s.Score, s.OpenAcresEst, s.FreightUse, s.InFloodZone, s.IsContaminated,
            s.NearPowerEdisonSite, s.InHighlandsPreservation, s.MilesToExit, s.PropClass, s.Utility,
            s.NearestHighway, s.SiteAddress, s.Municipality
        }), ct);
        var segments = await store.ToListAsync(store.Segments.OrderBy(s => s.Id), ct);

        var openAcres = rows.Select(r => r.OpenAcresEst).OrderBy(x => x).ToList();
        var kpis = new KpiDto(
            rows.Count, total,
            rows.Count == 0 ? 0 : Math.Round(rows.Average(r => r.Score), 1),
            rows.Count == 0 ? 0 : rows.Max(r => r.Score),
            openAcres.Count == 0 ? 0 : Math.Round(openAcres[openAcres.Count / 2], 2),
            rows.Count(r => r.FreightUse), rows.Count(r => r.InFloodZone), rows.Count(r => r.IsContaminated),
            rows.Count(r => r.NearPowerEdisonSite), rows.Count(r => r.InHighlandsPreservation),
            rows.Count(r => r.MilesToExit <= 0.5));

        var segmentStats = segments.Select(seg =>
        {
            var inSeg = rows.Where(r => r.Segment == seg.Id).ToList();
            var top = inSeg.OrderByDescending(r => r.Score).ThenBy(r => r.MilesToExit).FirstOrDefault();
            return new SegmentStatDto(seg.Id, seg.Counties, inSeg.Count,
                inSeg.Count == 0 ? 0 : Math.Round(inSeg.Average(r => r.Score), 1),
                top?.Score ?? 0, top?.Id, top?.SiteAddress, top?.Municipality,
                inSeg.Count(r => r.FreightUse), inSeg.Count(r => r.NearPowerEdisonSite),
                seg.PowerEdisonSitesInSegment);
        }).ToList();

        var histogram = Enumerable.Range(-1, 13).Select(i =>
        {
            var lo = i * 10;
            var label = i == 11 ? "110+" : $"{lo}–{lo + 9}";
            var count = rows.Count(r => i == 11 ? r.Score >= 110 : r.Score >= lo && r.Score < lo + 10);
            return new BucketDto(label, count);
        }).Where(b => b.Count > 0 || !b.Label.StartsWith('-')).ToList();

        var classes = rows.GroupBy(r => r.PropClass.Contains("4B") ? "Industrial (4B)"
                : r.PropClass == "1" || r.PropClass.StartsWith("1/") ? "Vacant (1)" : "Commercial (4A)")
            .Select(g => new BucketDto(g.Key, g.Count())).OrderByDescending(b => b.Count).ToList();
        var utilities = rows.GroupBy(r => r.Utility ?? "Unknown")
            .Select(g => new BucketDto(g.Key, g.Count())).OrderByDescending(b => b.Count).ToList();
        var highways = rows.GroupBy(r => r.NearestHighway ?? "Unknown")
            .Select(g => new BucketDto(g.Key, g.Count())).OrderByDescending(b => b.Count).ToList();

        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var days = ProposalDeadline.DayNumber - today.DayNumber;

        return new DashboardSummaryDto(kpis, segmentStats, histogram, classes, utilities, highways, days, ProposalDeadline);
    }
}
