using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NJTruckCharging.Application.Abstractions;
using NJTruckCharging.Application.Import;
using NJTruckCharging.Domain.Common;
using NJTruckCharging.Domain.Entities;

namespace NJTruckCharging.Infrastructure.Persistence;

public sealed class WriteStore(AppDbContext db, ILogger<WriteStore> logger) : IWriteStore
{
    private const int BatchSize = 1000;

    public async Task ReplaceAllAsync(ParsedWorkbook wb, ReimportScoring? scoring = null, CancellationToken ct = default)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            // Serialise with methodology preview/publish so scores are never written from two sources at once.
            if (!await ScoringLock.TryAcquireAsync(db, ct, timeoutMs: 30_000))
                throw new InvalidOperationException("A methodology publish or preview is running; retry the import shortly.");
            db.ChangeTracker.AutoDetectChangesEnabled = false;

            await db.NearbyFleets.ExecuteDeleteAsync(ct);
            await db.CandidateSites.ExecuteDeleteAsync(ct);
            await db.Segments.ExecuteDeleteAsync(ct);
            await db.Interchanges.ExecuteDeleteAsync(ct);
            await db.MethodologyNotes.ExecuteDeleteAsync(ct);
            await db.PowerEdisonSites.ExecuteDeleteAsync(ct);

            db.PowerEdisonSites.AddRange(PowerEdisonSeed.Sites.Select(Clone));
            db.Segments.AddRange(wb.Segments);
            db.Interchanges.AddRange(wb.Interchanges);
            db.MethodologyNotes.AddRange(wb.MethodologyNotes);
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();

            // Sites (fleets are inserted through the NearbyFleets navigation).
            for (var i = 0; i < wb.Sites.Count; i += BatchSize)
            {
                db.CandidateSites.AddRange(wb.Sites.Skip(i).Take(BatchSize));
                await db.SaveChangesAsync(ct);
                db.ChangeTracker.Clear();
                logger.LogInformation("Inserted {Count}/{Total} sites", Math.Min(i + BatchSize, wb.Sites.Count), wb.Sites.Count);
            }

            if (scoring is not null) await WriteReimportScoringAsync(scoring, wb.Sites.Count, ct);

            await tx.CommitAsync(ct);
            db.ChangeTracker.AutoDetectChangesEnabled = true;
        });
    }

    /// <summary>
    /// Inside the import transaction: stores the re-computed scores of the baseline and active versions, bumps the data
    /// version (so previews computed on the old data can no longer be published) and logs the re-application.
    /// </summary>
    private async Task WriteReimportScoringAsync(ReimportScoring scoring, int siteCount, CancellationToken ct)
    {
        db.ChangeTracker.AutoDetectChangesEnabled = true;
        var state = await db.MethodologyState.FirstOrDefaultAsync(s => s.Id == 1, ct);
        if (state is null) { state = new MethodologyState { Id = 1, DataVersion = 0 }; db.MethodologyState.Add(state); }
        state.DataVersion++;

        foreach (var set in scoring.ScoreSets)
        {
            await ScoreBulkWriter.ReplaceAsync(db, set.VersionId, set.Rows, ct);
            var version = await db.MethodologyVersions.FirstAsync(v => v.Id == set.VersionId, ct);
            version.PreviewRulesHash = version.RulesHash;
            version.ScoresDataVersion = state.DataVersion;
            version.PreviewedAtUtc = scoring.Now;
            version.PreviewSummaryJson = null;
        }

        if (scoring.ActiveVersionId is { } active)
        {
            state.ActiveVersionId = active;
            state.ActivatedAtUtc = scoring.Now;
            db.MethodologyActivations.Add(new MethodologyActivation
            {
                VersionId = active, PreviousVersionId = active, Reason = ActivationReason.Reimport, AtUtc = scoring.Now,
                SitesScored = siteCount, DataVersion = state.DataVersion,
                Note = "Workbook re-import: active methodology re-applied to the new data."
            });
        }
        await db.SaveChangesAsync(ct);
    }

    public Task<List<CandidateSite>> LoadSitesForUpdateAsync(CancellationToken ct = default) =>
        db.CandidateSites.ToListAsync(ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);

    public Task<bool> HasSitesAsync(CancellationToken ct = default) => db.CandidateSites.AnyAsync(ct);

    public async Task UpdateScoreStatisticsAsync(CancellationToken ct = default)
    {
        // PostgreSQL (cloud): skipped. Only the table owner may ANALYZE, the API's logins are deliberately not owners, and
        // autovacuum re-analyzes CandidateSites by itself after a publish rewrites ~30k rows.
        if (db.Database.IsPostgres()) return;
        if (!db.Database.IsSqlServer()) return;
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE STATISTICS dbo.CandidateSites (IX_CandidateSites_Score_MilesToExit, IX_CandidateSites_Segment_ComputedRank) WITH FULLSCAN;", ct);
        });
    }

    public async Task UpdateStatisticsAsync(CancellationToken ct = default)
    {
        if (db.Database.IsPostgres()) { await db.Database.ExecuteSqlRawAsync("ANALYZE \"CandidateSites\"; ANALYZE \"NearbyFleets\";", ct); return; }
        // SQL Server only: on other providers (e.g. in tests) this is a no-op.
        if (!db.Database.IsSqlServer())
            return;

        logger.LogInformation("Refreshing statistics on CandidateSites and NearbyFleets …");
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE STATISTICS dbo.CandidateSites WITH FULLSCAN; UPDATE STATISTICS dbo.NearbyFleets WITH FULLSCAN;", ct);
        });
    }

    private static PowerEdisonSite Clone(PowerEdisonSite p) => new()
    {
        Name = p.Name, ShortName = p.ShortName, Address = p.Address, Municipality = p.Municipality,
        Segment = p.Segment, Status = p.Status, Notes = p.Notes, Latitude = p.Latitude, Longitude = p.Longitude
    };
}
