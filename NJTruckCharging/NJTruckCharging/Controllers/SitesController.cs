using Microsoft.AspNetCore.Mvc;
using NJTruckCharging.Application.Common;
using NJTruckCharging.Application.Sites;

namespace NJTruckCharging.Controllers;

[ApiController]
[Route("api/sites")]
public class SitesController(SiteQueryService sites) : ControllerBase
{
    /// <summary>Every site matching the filter, as compact points for the map layer.</summary>
    [HttpGet("map")]
    public Task<List<SiteMapPointDto>> Map([FromQuery] SiteFilter filter, CancellationToken ct) =>
        sites.GetMapPointsAsync(filter, ct);

    /// <summary>Ranked, paged list of sites matching the filter.</summary>
    [HttpGet]
    public Task<PagedResult<SiteListItemDto>> List([FromQuery] SiteFilter filter, [FromQuery] SiteListQuery query, CancellationToken ct) =>
        sites.GetPageAsync(filter, query, ct);

    /// <summary>Everything known about a site, its score breakdown and the largest fleets nearby.</summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<SiteDetailDto>> Get(int id, CancellationToken ct) =>
        await sites.GetDetailAsync(id, ct) is { } detail ? detail : NotFound();

    /// <summary>CSV export of the filtered sites. <c>format=research</c> exports the research shortlist instead
    /// (parcel evidence, verified vs preliminary data, professional contact, sources, missing verification).</summary>
    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] SiteFilter filter, [FromQuery] string? format, CancellationToken ct) =>
        string.Equals(format, "research", StringComparison.OrdinalIgnoreCase)
            ? File(await sites.ExportResearchCsvAsync(filter, ct), "text/csv", $"c3-research-shortlist-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv")
            : File(await sites.ExportCsvAsync(filter, ct), "text/csv", $"c3-sites-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv");
}
