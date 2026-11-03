using Microsoft.AspNetCore.Mvc;
using NJTruckCharging.Application.Reference;
using NJTruckCharging.Domain.Entities;
using NJTruckCharging.Domain.Scoring;

namespace NJTruckCharging.Controllers;

[ApiController]
[Route("api")]
public class ReferenceController(ReferenceService reference, IConfiguration configuration) : ControllerBase
{
    [HttpGet("segments")]
    public Task<List<SegmentInfo>> Segments(CancellationToken ct) => reference.GetSegmentsAsync(ct);

    /// <summary>EV Edison / Power Edison sites (operating hub and pipeline sites).</summary>
    [HttpGet("power-edison-sites")]
    public Task<List<PowerEdisonSiteDto>> PowerEdisonSites(CancellationToken ct) => reference.GetPowerEdisonSitesAsync(ct);

    [HttpGet("interchanges")]
    public Task<List<Interchange>> Interchanges(CancellationToken ct) => reference.GetInterchangesAsync(ct);

    [HttpGet("methodology")]
    public Task<List<MethodologyNote>> Methodology(CancellationToken ct) => reference.GetMethodologyAsync(ct);

    [HttpGet("scoring-rules")]
    public ScoringRules ScoringRules() => Domain.Scoring.ScoringRules.Default;

    [HttpGet("filter-options")]
    public Task<FilterOptionsDto> FilterOptions(CancellationToken ct) => reference.GetFilterOptionsAsync(ct);

    /// <summary>Browser-side Google Maps settings (the key is restricted by HTTP referrer in Google Cloud).</summary>
    [HttpGet("config/maps")]
    public object MapsConfig() => new
    {
        apiKey = configuration["GoogleMaps:ApiKey"] ?? "",
        mapId = configuration["GoogleMaps:MapId"] ?? "DEMO_MAP_ID"
    };
}
