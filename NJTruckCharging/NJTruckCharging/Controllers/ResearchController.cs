using Microsoft.AspNetCore.Mvc;
using NJTruckCharging.Application.Research;

namespace NJTruckCharging.Controllers;

/// <summary>
/// Research quality (Phase 1): append-only research history, "Start research", per-site completeness of the
/// eight core checks, the review queue and per-segment progress. Independent of the C3 score. Not a CRM.
/// </summary>
[ApiController]
[Route("api/research")]
public class ResearchController(ResearchService research) : ControllerBase
{
    /// <summary>Append-only history for a site, newest first.</summary>
    [HttpGet("history")]
    public async Task<ActionResult<List<ResearchHistoryEntryDto>>> History([FromQuery] string siteId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(siteId)) return BadRequest("siteId is required.");
        return await research.GetHistoryAsync(siteId, ct);
    }

    /// <summary>Completeness, check states and open review issues for one site.</summary>
    [HttpGet("site")]
    public async Task<ActionResult<SiteResearchDto>> Site([FromQuery] string siteId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(siteId)) return BadRequest("siteId is required.");
        return await research.GetSiteAsync(siteId, ct) is { } dto ? dto : NotFound($"No candidate with SiteId '{siteId}'.");
    }

    /// <summary>Adds a candidate to the research workflow (idempotent).</summary>
    [HttpPost("start")]
    public async Task<ActionResult<SiteResearchDto>> Start([FromBody] StartResearchRequest req, CancellationToken ct)
    {
        var (outcome, dto) = await research.StartAsync(req, ct);
        return outcome switch
        {
            ResearchService.StartOutcome.CandidateNotFound => NotFound($"No candidate with SiteId '{req.SiteId}'."),
            _ => Ok(dto)
        };
    }

    /// <summary>Research-set sites with unresolved verification issues. issues: ParcelMismatch, BusinessConflict, …</summary>
    [HttpGet("queue")]
    public Task<ReviewQueueDto> Queue([FromQuery] int[]? segments, [FromQuery] string[]? issues, CancellationToken ct) =>
        research.GetQueueAsync(segments, issues, ct);

    /// <summary>Research completeness per C3 segment (research-set sites only).</summary>
    [HttpGet("summary")]
    public Task<ResearchSummaryDto> Summary([FromQuery] int[]? segments, CancellationToken ct) =>
        research.GetSummaryAsync(segments, ct);
}
