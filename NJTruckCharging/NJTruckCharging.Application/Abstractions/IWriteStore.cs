using NJTruckCharging.Application.Import;
using NJTruckCharging.Domain.Entities;

namespace NJTruckCharging.Application.Abstractions;

public interface IWriteStore
{
    /// <summary>Deletes all imported data and inserts the parsed workbook.</summary>
    Task ReplaceAllAsync(ParsedWorkbook workbook, CancellationToken ct = default);

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
}
