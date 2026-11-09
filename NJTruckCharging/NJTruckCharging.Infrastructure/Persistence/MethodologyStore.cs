using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using NJTruckCharging.Application.Abstractions;
using NJTruckCharging.Domain.Entities;
using NJTruckCharging.Domain.Methodology;

namespace NJTruckCharging.Infrastructure.Persistence;

public sealed class MethodologyStore(AppDbContext db, IActivationHook hook, ILogger<MethodologyStore> logger) : IMethodologyStore
{
    public Task<MethodologyState?> GetStateAsync(CancellationToken ct = default) =>
        db.MethodologyState.AsNoTracking().FirstOrDefaultAsync(s => s.Id == 1, ct);

    public Task<MethodologyVersion?> FindVersionAsync(int id, CancellationToken ct = default) =>
        db.MethodologyVersions.FirstOrDefaultAsync(v => v.Id == id, ct);

    public Task<MethodologyVersion?> FindBaselineAsync(CancellationToken ct = default) =>
        db.MethodologyVersions.FirstOrDefaultAsync(v => v.IsBaseline, ct);

    public async Task AddVersionAsync(MethodologyVersion version, CancellationToken ct = default)
    {
        db.MethodologyVersions.Add(version);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteVersionAsync(MethodologyVersion version, CancellationToken ct = default)
    {
        await db.SiteVersionScores.Where(s => s.MethodologyVersionId == version.Id).ExecuteDeleteAsync(ct);
        db.MethodologyVersions.Remove(version);
        await db.SaveChangesAsync(ct);
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);

    public async Task EnsureStateAsync(CancellationToken ct = default)
    {
        if (await db.MethodologyState.AnyAsync(ct)) return;
        db.MethodologyState.Add(new MethodologyState { Id = 1, DataVersion = 1 });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Only the columns scoring, ranking and the preview need — and NO SQL ORDER BY. Selecting every column (several are
    /// nvarchar(max)) with a sort made SQL Server request a huge memory grant; on LocalDB that waited indefinitely on
    /// RESOURCE_SEMAPHORE under memory pressure (the same failure /api/sites once had). Sorted in memory instead.
    /// </summary>
    public async Task<List<CandidateSite>> LoadSitesForScoringAsync(CancellationToken ct = default)
    {
        var rows = await db.CandidateSites.AsNoTracking().Select(s => new CandidateSite
        {
            Id = s.Id, SiteId = s.SiteId, Segment = s.Segment, RankInSegment = s.RankInSegment,
            SiteAddress = s.SiteAddress, Municipality = s.Municipality, County = s.County, PamsPin = s.PamsPin,
            Score = s.Score, ComputedRank = s.ComputedRank, ScoreVersionId = s.ScoreVersionId,
            Acres = s.Acres, RecordedAcres = s.RecordedAcres, OpenAcresEst = s.OpenAcresEst, BuildingCoverage = s.BuildingCoverage,
            PropClass = s.PropClass, ShortSideFt = s.ShortSideFt, Compactness = s.Compactness, LotsInSite = s.LotsInSite,
            YearBuilt = s.YearBuilt, LandValue = s.LandValue, ImprovementValue = s.ImprovementValue, AnnualTax = s.AnnualTax,
            LastSalePrice = s.LastSalePrice, OwnerOccupiedHint = s.OwnerOccupiedHint,
            FreightUse = s.FreightUse, IndustrialUse = s.IndustrialUse, BusyMultiTenant = s.BusyMultiTenant,
            TrucksBased3Mi = s.TrucksBased3Mi, TrucksBased5Mi = s.TrucksBased5Mi, TrucksBased10Mi = s.TrucksBased10Mi,
            CarriersBased5Mi = s.CarriersBased5Mi, TractorsBased5Mi = s.TractorsBased5Mi,
            TruckAadtAdjacent = s.TruckAadtAdjacent, TruckAadtAtExit = s.TruckAadtAtExit, MilesToTruckRoad = s.MilesToTruckRoad,
            LargeWarehouses2Mi = s.LargeWarehouses2Mi, TruckStops2Mi = s.TruckStops2Mi, MilesToTruckStop = s.MilesToTruckStop,
            MilesToPort = s.MilesToPort, MilesToExit = s.MilesToExit, MilesToTruckDcfc = s.MilesToTruckDcfc,
            MilesToAnyDcfc = s.MilesToAnyDcfc, MilesToPowerEdisonSite = s.MilesToPowerEdisonSite,
            Utility = s.Utility, Highlands = s.Highlands, FloodZone100Yr = s.FloodZone100Yr, KnownContaminatedSites = s.KnownContaminatedSites,
            FloodChecked = s.FloodChecked, InFloodZone = s.InFloodZone, InHighlandsPreservation = s.InHighlandsPreservation,
            IsContaminated = s.IsContaminated, NearPowerEdisonSite = s.NearPowerEdisonSite,
        }).ToListAsync(ct);
        rows.Sort((a, b) => a.Segment != b.Segment ? a.Segment.CompareTo(b.Segment) : string.CompareOrdinal(a.SiteId, b.SiteId));
        return rows;
    }

    public async Task<ActivationFailure> StoreScoresAsync(int versionId, IReadOnlyList<SiteVersionScore> rows, string rulesHash,
        int dataVersion, string? summaryJson, DateTimeOffset now, CancellationToken ct = default)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            if (!await ScoringLock.TryAcquireAsync(db, ct)) return ActivationFailure.ConcurrentOperation;

            var state = await db.MethodologyState.FirstAsync(s => s.Id == 1, ct);
            if (state.DataVersion != dataVersion) return ActivationFailure.PreviewStale; // re-import happened meanwhile
            var version = await db.MethodologyVersions.FirstOrDefaultAsync(v => v.Id == versionId, ct);
            if (version is null) return ActivationFailure.NotFound;
            if (version.RulesHash != rulesHash) return ActivationFailure.PreviewStale;   // rules edited meanwhile

            await ScoreBulkWriter.ReplaceAsync(db, versionId, rows, ct);
            version.PreviewRulesHash = rulesHash;
            version.ScoresDataVersion = dataVersion;
            version.PreviewedAtUtc = now;
            version.PreviewSummaryJson = summaryJson;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ActivationFailure.None;
        });
    }

    public async Task<ActivateResult> ActivateAsync(ActivateCommand cmd, CancellationToken ct = default)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        try
        {
            return await strategy.ExecuteAsync(async () =>
            {
                db.ChangeTracker.Clear();
                await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
                if (!await ScoringLock.TryAcquireAsync(db, ct))
                    return Fail(ActivationFailure.ConcurrentOperation, "Another methodology publish, preview or import is running. Try again shortly.");

                var state = await db.MethodologyState.FirstAsync(s => s.Id == 1, ct);
                if (cmd.ExpectedActiveVersionId is { } expected && state.ActiveVersionId != expected)
                    return Fail(ActivationFailure.ActiveVersionChanged,
                        $"The active methodology changed (now version {state.ActiveVersionId}). Re-run the preview against it.");

                var version = await db.MethodologyVersions.FirstOrDefaultAsync(v => v.Id == cmd.VersionId, ct);
                if (version is null) return Fail(ActivationFailure.NotFound, "Version not found.");
                if (version.RulesHash != cmd.ExpectedRulesHash || version.PreviewRulesHash != version.RulesHash)
                    return Fail(ActivationFailure.PreviewStale, "The rules changed since the preview. Preview again before publishing.");
                if (version.ScoresDataVersion != state.DataVersion)
                    return Fail(ActivationFailure.PreviewStale, "The candidate data changed (workbook re-import) since the preview. Preview again.");

                var siteCount = await db.CandidateSites.CountAsync(ct);
                var scoreCount = await db.SiteVersionScores.CountAsync(s => s.MethodologyVersionId == version.Id, ct);
                var uncovered = await db.CandidateSites.CountAsync(c =>
                    !db.SiteVersionScores.Any(s => s.MethodologyVersionId == version.Id && s.SiteId == c.SiteId), ct);
                if (scoreCount != siteCount || uncovered != 0)
                    return Fail(ActivationFailure.ScoresIncomplete,
                        $"Proposed scores cover {siteCount - uncovered:N0} of {siteCount:N0} sites ({scoreCount:N0} rows). Preview again.");

                var updated = await MaterializeAsync(db, version.Id, ct);
                if (updated != siteCount)
                    throw new InvalidOperationException($"Materialised {updated} of {siteCount} sites.");

                await hook.BeforeCommitAsync(version.Id, ct);

                var previous = state.ActiveVersionId;
                if (previous != version.Id) state.PreviousActiveVersionId = previous;
                state.ActiveVersionId = version.Id;
                state.ActivatedAtUtc = cmd.Now;
                version.Status = MethodologyVersionStatus.Published;
                version.PublishedAtUtc ??= cmd.Now;
                db.MethodologyActivations.Add(new MethodologyActivation
                {
                    VersionId = version.Id, PreviousVersionId = previous, Reason = cmd.Reason, AtUtc = cmd.Now,
                    SitesScored = updated, DataVersion = state.DataVersion, Note = cmd.Note
                });
                await db.SaveChangesAsync(ct);   // RowVersion guards the pointer against concurrent writers
                await tx.CommitAsync(ct);
                return new ActivateResult(ActivationFailure.None, null, updated, previous);
            });
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail(ActivationFailure.ActiveVersionChanged, "The active methodology was changed concurrently. Re-run the preview.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Methodology activation of version {Version} failed and was rolled back", cmd.VersionId);
            return Fail(ActivationFailure.Failed, "Publishing failed and was rolled back; the active methodology is unchanged. " + ex.Message);
        }
        finally
        {
            db.ChangeTracker.Clear();
        }

        static ActivateResult Fail(ActivationFailure f, string msg) => new(f, msg, 0, null);
    }

    /// <summary>One set-based UPDATE copying a version's scores onto the live columns (inside the caller's transaction).</summary>
    internal static Task<int> MaterializeAsync(AppDbContext db, int versionId, CancellationToken ct)
    {
        var cols = string.Join(",\n    ", BaselineMethodology.BreakdownColumns.Values.Select(c => $"c.[Breakdown_{c}] = v.[Breakdown_{c}]"));
        var sql = $"""
            UPDATE c SET
                c.[Score] = v.[Score], c.[ComputedRank] = v.[Rank], c.[TruckDemandPoints] = v.[TruckDemandPoints],
                c.[ScoreVersionId] = v.[MethodologyVersionId],
                {cols}
            FROM [CandidateSites] c
            JOIN [SiteVersionScores] v ON v.[SiteId] = c.[SiteId] AND v.[MethodologyVersionId] = @p0
            """;
        return db.Database.ExecuteSqlRawAsync(sql, [versionId], ct);
    }
}

/// <summary>
/// Serialises every operation that writes scores (preview, publish, restore, re-import, rescore) with a
/// transaction-scoped SQL Server application lock, so two methodology publications can never interleave.
/// </summary>
internal static class ScoringLock
{
    public const string Resource = "njtc-methodology-scoring";

    public static async Task<bool> TryAcquireAsync(AppDbContext db, CancellationToken ct, int timeoutMs = 3000)
    {
        if (db.Database.ProviderName != "Microsoft.EntityFrameworkCore.SqlServer") return true;
        var result = await db.Database.SqlQueryRaw<int>(
            "DECLARE @r int; EXEC @r = sp_getapplock @Resource = {0}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = {1}; SELECT @r AS [Value];",
            Resource, timeoutMs).ToListAsync(ct);
        return result.Count == 1 && result[0] >= 0;
    }
}

/// <summary>
/// Replaces a version's SiteVersionScores rows inside the current transaction with parameterized multi-row INSERTs.
/// SqlBulkCopy was deliberately NOT used: SQL Server sized its memory grant for the whole bulk load (50–60 MB measured),
/// and on a memory-starved LocalDB that grant waited minutes on RESOURCE_SEMAPHORE — blocking even tiny queries queued
/// behind it. Small INSERT batches need little or no query memory, so previews/publishes never stall on memory.
/// </summary>
internal static class ScoreBulkWriter
{
    /// <summary>Rows per INSERT statement: 90 rows × 23 parameters stays under SQL Server's 2,100-parameter limit.</summary>
    private const int RowsPerStatement = 90;

    private static readonly (string Column, System.Reflection.PropertyInfo Prop)[] BreakdownProps =
        BaselineMethodology.BreakdownColumns.Values
            .Select(c => ($"Breakdown_{c}", typeof(Domain.Scoring.ScoreBreakdown).GetProperty(c)!)).ToArray();

    private static readonly string ColumnList = string.Join(", ",
        new[] { "MethodologyVersionId", "SiteId", "Segment", "Score", "Rank", "TruckDemandPoints", "PointsJson" }
            .Concat(BreakdownProps.Select(b => b.Column)).Select(c => $"[{c}]"));

    /// <summary>
    /// Parameterized multi-row INSERTs on the context's connection/transaction (plain ADO.NET so nothing is ever
    /// string-formatted). One cached plan, about 1 MB of query memory per statement.
    /// </summary>
    public static async Task ReplaceAsync(AppDbContext db, int versionId, IReadOnlyList<SiteVersionScore> rows, CancellationToken ct)
    {
        await db.SiteVersionScores.Where(s => s.MethodologyVersionId == versionId).ExecuteDeleteAsync(ct);
        var perRow = 7 + BreakdownProps.Length;
        var conn = db.Database.GetDbConnection();
        var tx = db.Database.CurrentTransaction?.GetDbTransaction();
        string? fullSql = null;
        for (var i = 0; i < rows.Count; i += RowsPerStatement)
        {
            var n = Math.Min(RowsPerStatement, rows.Count - i);
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandTimeout = 120;
            cmd.CommandText = n == RowsPerStatement ? fullSql ??= BuildSql(RowsPerStatement, perRow) : BuildSql(n, perRow);
            for (var k = 0; k < n; k++)
            {
                var r = rows[i + k];
                var o = k * perRow;
                Add(cmd, o, versionId); Add(cmd, o + 1, r.SiteId); Add(cmd, o + 2, r.Segment); Add(cmd, o + 3, r.Score);
                Add(cmd, o + 4, r.Rank); Add(cmd, o + 5, r.TruckDemandPoints); Add(cmd, o + 6, r.PointsJson);
                for (var b = 0; b < BreakdownProps.Length; b++) Add(cmd, o + 7 + b, (double)BreakdownProps[b].Prop.GetValue(r.Breakdown)!);
            }
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    private static string BuildSql(int rows, int perRow)
    {
        var sql = new System.Text.StringBuilder($"INSERT INTO [SiteVersionScores] ({ColumnList}) VALUES ");
        for (var k = 0; k < rows; k++)
        {
            sql.Append(k == 0 ? "(" : ", (");
            for (var c = 0; c < perRow; c++) sql.Append(c == 0 ? "@p" : ", @p").Append(k * perRow + c);
            sql.Append(')');
        }
        return sql.ToString();
    }

    private static void Add(System.Data.Common.DbCommand cmd, int i, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = "@p" + i;
        p.Value = value;
        if (value is string) { p.DbType = DbType.String; p.Size = 4000; }   // fixed size → one reusable plan
        cmd.Parameters.Add(p);
    }
}
