using NJTruckCharging.Application.Import;
using NJTruckCharging.Domain.Entities;

namespace NJTruckCharging.Application.Abstractions;

public interface IWriteStore
{
    /// <summary>
    /// Deletes all imported data and inserts the parsed workbook. With <paramref name="scoring"/>, the re-computed
    /// methodology scores are written in the same transaction (the live columns are already set on the sites).
    /// </summary>
    Task ReplaceAllAsync(ParsedWorkbook workbook, ReimportScoring? scoring = null, CancellationToken ct = default);

    /// <summary>Loads every candidate site with change tracking, for re-scoring.</summary>
    Task<List<CandidateSite>> LoadSitesForUpdateAsync(CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);

    Task<bool> HasSitesAsync(CancellationToken ct = default);

    /// <summary>
    /// Refreshes query-optimizer statistics after a bulk replace or rescore so the paged
    /// <c>/api/sites</c> query keeps a good plan (stale stats after a mass delete+insert caused
    /// 180s timeouts). No-op on providers other than SQL Server.
    /// </summary>
    Task UpdateStatisticsAsync(CancellationToken ct = default);

    /// <summary>
    /// After a methodology publish only Score/ComputedRank changed: refresh just the two score indexes' statistics
    /// (narrow keys → small memory grant) instead of a FULLSCAN of every column of the wide CandidateSites table, which
    /// waited ~75 s for memory on a memory-starved LocalDB.
    /// </summary>
    Task UpdateScoreStatisticsAsync(CancellationToken ct = default);
}
