using NJTruckCharging.Application.Abstractions;
using NJTruckCharging.Domain.Entities;

namespace NJTruckCharging.Application.Reference;

public sealed record PowerEdisonSiteDto(
    int Id, string Name, string ShortName, string? Address, string Municipality, int Segment,
    string Status, string? Notes, double Lat, double Lng, int CandidatesWithinTwoMiles);

public sealed record FilterOptionsDto(
    IReadOnlyList<string> Counties, IReadOnlyList<string> Utilities, IReadOnlyList<string> Highways);

public sealed class ReferenceService(IReadStore store)
{
    public Task<List<SegmentInfo>> GetSegmentsAsync(CancellationToken ct = default) =>
        store.ToListAsync(store.Segments.OrderBy(s => s.Id), ct);

    public async Task<List<PowerEdisonSiteDto>> GetPowerEdisonSitesAsync(CancellationToken ct = default)
    {
        var sites = await store.ToListAsync(store.PowerEdisonSites.OrderBy(p => p.Id), ct);
        var near = await store.ToListAsync(store.Sites.Where(s => s.NearPowerEdisonSite)
            .Select(s => s.NearestPowerEdisonSite), ct);
        return sites.Select(p => new PowerEdisonSiteDto(p.Id, p.Name, p.ShortName, p.Address, p.Municipality, p.Segment,
            p.Status.ToString(), p.Notes, p.Latitude, p.Longitude, near.Count(n => n == p.Name))).ToList();
    }

    public Task<List<MethodologyNote>> GetMethodologyAsync(CancellationToken ct = default) =>
        store.ToListAsync(store.MethodologyNotes.OrderBy(m => m.SortOrder), ct);

    public Task<List<Interchange>> GetInterchangesAsync(CancellationToken ct = default) =>
        store.ToListAsync(store.Interchanges.OrderBy(i => i.Highway).ThenBy(i => i.Exit), ct);

    public async Task<FilterOptionsDto> GetFilterOptionsAsync(CancellationToken ct = default)
    {
        var counties = await store.ToListAsync(store.Sites.Select(s => s.County).Distinct().OrderBy(x => x), ct);
        var utilities = await store.ToListAsync(store.Sites.Where(s => s.Utility != null).Select(s => s.Utility!).Distinct().OrderBy(x => x), ct);
        var highways = await store.ToListAsync(store.Sites.Where(s => s.NearestHighway != null).Select(s => s.NearestHighway!).Distinct().OrderBy(x => x), ct);
        return new FilterOptionsDto(counties, utilities, highways);
    }
}
