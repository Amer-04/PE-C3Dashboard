using Microsoft.AspNetCore.Mvc;
using NJTruckCharging.Application.Comparison;

namespace NJTruckCharging.Controllers;

/// <summary>
/// Side-by-side comparison of 2–5 candidates by stable SiteId (repeat <c>siteIds</c> or pass a comma list).
/// Read-only; highlights differences without declaring a winner.
/// </summary>
[ApiController]
[Route("api/compare")]
public class CompareController(SiteComparisonService comparison) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<SiteComparisonDto>> Compare([FromQuery] string[] siteIds, CancellationToken ct)
    {
        var (ids, error) = SiteComparisonService.Normalise(siteIds);
        if (error is not null) return BadRequest(error);
        return await comparison.CompareAsync(ids, ct);
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] string[] siteIds, CancellationToken ct)
    {
        var (ids, error) = SiteComparisonService.Normalise(siteIds);
        if (error is not null) return BadRequest(error);
        var dto = await comparison.CompareAsync(ids, ct);
        return File(SiteComparisonService.ToCsv(dto), "text/csv", $"c3-site-comparison-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv");
    }
}
