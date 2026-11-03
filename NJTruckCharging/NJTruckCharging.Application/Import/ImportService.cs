using System.Diagnostics;
using Microsoft.Extensions.Logging;
using NJTruckCharging.Application.Abstractions;
using NJTruckCharging.Domain.Scoring;

namespace NJTruckCharging.Application.Import;

public sealed class ImportService(
    IWorkbookReader reader,
    IWriteStore writeStore,
    ILogger<ImportService> logger)
{
    public async Task<ImportResultDto> ImportAsync(Stream xlsx, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var wb = reader.Read(xlsx);
        logger.LogInformation("Parsed workbook: {Sites} sites, {Fleets} fleets", wb.Sites.Count, wb.NearbyFleets.Count);

        foreach (var s in wb.Sites) s.RefreshDerivedFlags();

        // 1) Score without the "busy multi-tenant" flag (not stored in the workbook) ...
        SiteScorer.ScoreAll(wb.Sites);
        // 2) ... recover it from the delivered score, then 3) score again.
        var busy = 0;
        foreach (var s in wb.Sites)
        {
            if (SiteScorer.InferBusyMultiTenant(s, s.FileScore)) { s.BusyMultiTenant = true; busy++; }
        }
        SiteScorer.ScoreAll(wb.Sites);

        var maxDiff = wb.Sites.Count == 0 ? 0 : wb.Sites.Max(s => Math.Abs(s.Breakdown.Total - s.FileScore));
        var rankMismatches = wb.Sites.Count(s => s.ComputedRank != s.RankInSegment);
        if (maxDiff > 0.06 || rankMismatches > 0)
            logger.LogWarning("Scoring parity: max diff {Diff:F3}, rank mismatches {Mismatch}", maxDiff, rankMismatches);

        // Link fleets to their site by (segment, rank in segment) – how the "Nearby fleets" sheet keys them.
        var byRank = wb.Sites.ToDictionary(s => (s.Segment, s.RankInSegment));
        var linked = 0;
        foreach (var f in wb.NearbyFleets)
        {
            if (byRank.TryGetValue((f.Segment, f.RankInSegment), out var site))
            {
                site.NearbyFleets.Add(f);
                f.CandidateSite = site;
                linked++;
            }
        }
        wb.NearbyFleets.RemoveAll(f => f.CandidateSite is null);

        await writeStore.ReplaceAllAsync(wb, ct);
        // Mass delete+insert leaves statistics stale; refresh them so /api/sites keeps a good plan.
        await writeStore.UpdateStatisticsAsync(ct);
        sw.Stop();

        var result = new ImportResultDto(wb.Sites.Count, wb.NearbyFleets.Count, linked, wb.Segments.Count,
            wb.Interchanges.Count, busy, Math.Round(maxDiff, 3), rankMismatches, sw.Elapsed);
        logger.LogInformation("Import finished: {@Result}", result);
        return result;
    }

    /// <summary>Re-runs the scoring engine over the stored sites (e.g. after editing a site's attributes).</summary>
    public async Task<int> RescoreAsync(ScoringRules? rules = null, CancellationToken ct = default)
    {
        var sites = await writeStore.LoadSitesForUpdateAsync(ct);
        foreach (var s in sites) s.RefreshDerivedFlags();
        SiteScorer.ScoreAll(sites, rules);
        await writeStore.SaveChangesAsync(ct);
        // Scores changed on every row; refresh statistics for the Score-ordered queries.
        await writeStore.UpdateStatisticsAsync(ct);
        return sites.Count;
    }
}
