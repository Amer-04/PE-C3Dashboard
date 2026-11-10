using System.Diagnostics;
using Microsoft.Extensions.Logging;
using NJTruckCharging.Application.Abstractions;
using NJTruckCharging.Application.Methodology;
using NJTruckCharging.Domain.Entities;
using NJTruckCharging.Domain.Methodology;
using NJTruckCharging.Domain.Scoring;

namespace NJTruckCharging.Application.Import;

public sealed class ImportService(
    IWorkbookReader reader,
    IWriteStore writeStore,
    IMethodologyStore methodologyStore,
    ICustomFieldStore customFieldStore,
    MethodologyService methodology,
    ActiveMethodologyCache activeCache,
    TimeProvider clock,
    ILogger<ImportService> logger)
{
    public async Task<ImportResultDto> ImportAsync(Stream xlsx, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var wb = reader.Read(xlsx);
        logger.LogInformation("Parsed workbook: {Sites} sites, {Fleets} fleets", wb.Sites.Count, wb.NearbyFleets.Count);

        ValidateData(wb);
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

        // Baseline (Version 1) scores are always recomputed; a custom active methodology is re-applied to the new data
        // (only if its rules still validate) and becomes the live score — all written in the import transaction.
        var (scoring, activeId, custom) = await PrepareMethodologyAsync(wb.Sites, ct);

        await writeStore.ReplaceAllAsync(wb, scoring, ct);
        // Mass delete+insert leaves statistics stale; refresh them so /api/sites keeps a good plan.
        await writeStore.UpdateStatisticsAsync(ct);
        sw.Stop();

        if (scoring is null) await methodology.EnsureInitializedAsync(ct);   // first import into an empty database
        await methodology.RefreshCacheAsync(ct);

        var result = new ImportResultDto(wb.Sites.Count, wb.NearbyFleets.Count, linked, wb.Segments.Count,
            wb.Interchanges.Count, busy, Math.Round(maxDiff, 3), rankMismatches, sw.Elapsed, activeId ?? activeCache.Current?.VersionId, custom);
        logger.LogInformation("Import finished: {@Result}", result);
        return result;
    }

    /// <summary>Structural checks: the import is rejected (nothing changes) unless every site can be scored.</summary>
    private static void ValidateData(ParsedWorkbook wb)
    {
        if (wb.Sites.Count == 0) throw new ImportValidationException("The workbook contains no candidate sites.");
        var dup = wb.Sites.GroupBy(s => s.SiteId, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (dup is not null) throw new ImportValidationException($"Duplicate site_id '{dup.Key}' — Site IDs must be unique and stable.");
        var badSeg = wb.Sites.FirstOrDefault(s => s.Segment is < 1 or > 6);
        if (badSeg is not null) throw new ImportValidationException($"Site {badSeg.SiteId} has segment {badSeg.Segment}; expected 1–6.");
        foreach (var s in wb.Sites)
            foreach (var f in ScoringFieldCatalog.All.Where(f => f.Type == FieldType.Number))
                if (f.Read(s).Number is { } n && !double.IsFinite(n))
                    throw new ImportValidationException($"Site {s.SiteId}: '{f.Label}' is not a finite number.");
    }

    private async Task<(ReimportScoring? Scoring, int? ActiveId, bool Custom)> PrepareMethodologyAsync(List<CandidateSite> sites, CancellationToken ct)
    {
        var baseline = await methodologyStore.FindBaselineAsync(ct);
        foreach (var s in sites) s.TruckDemandPoints = s.Breakdown.TruckDemand;   // baseline values (SiteScorer)
        if (baseline is null) return (null, null, false);                         // empty DB: Version 1 is created after the import

        var state = await methodologyStore.GetStateAsync(ct);
        var baselineRules = MethodologyService.RulesOf(baseline);
        var baselineResult = MethodologyEngine.Evaluate(sites, baselineRules);
        var sets = new List<VersionScoreSet> { new(baseline.Id, MethodologyScoring.ToRows(baseline.Id, baselineRules, baselineResult)) };

        var activeId = state?.ActiveVersionId ?? baseline.Id;
        var custom = false;
        if (activeId != baseline.Id)
        {
            var active = await methodologyStore.FindVersionAsync(activeId, ct)
                ?? throw new ImportValidationException($"Active methodology version {activeId} not found.");
            var rules = MethodologyService.RulesOf(active);
            // Custom values are soft-keyed by SiteId, so they survive the re-import and are re-applied with the rules.
            var catalog = await CustomCatalog.BuildAsync(customFieldStore, null, ct);
            var errors = MethodologyValidator.Validate(rules, catalog).Where(i => i.Severity == IssueSeverity.Error).ToList();
            if (errors.Count > 0)
                throw new ImportValidationException($"The active methodology (v{active.Id}) is not valid for the new data: " +
                    string.Join("; ", errors.Select(e => e.Message)) + " Nothing was imported.");
            var result = MethodologyEngine.Evaluate(sites, rules, catalog);
            MethodologyScoring.ApplyToSites(sites, active.Id, rules, result);
            sets.Add(new VersionScoreSet(active.Id, MethodologyScoring.ToRows(active.Id, rules, result)));
            custom = true;
            logger.LogInformation("Re-applied active methodology v{Id} '{Name}' to {Sites} imported sites", active.Id, active.Name, sites.Count);
        }
        else
        {
            MethodologyScoring.ApplyToSites(sites, baseline.Id, baselineRules, baselineResult);
        }
        return (new ReimportScoring(activeId, sets, clock.GetUtcNow()), activeId, custom);
    }
}
