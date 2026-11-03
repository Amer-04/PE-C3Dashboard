using Microsoft.AspNetCore.Mvc;
using NJTruckCharging.Application.Screening;

namespace NJTruckCharging.Controllers;

/// <summary>
/// Site screening decisions (Promising / Needs more research / Unsuitable / Not reviewed) with evidence.
/// Suitability only — not a CRM pipeline; never affects the C3 score.
/// </summary>
[ApiController]
[Route("api/screening")]
public class ScreeningController(ScreeningService screening) : ControllerBase
{
    /// <summary>Current decision + history for a site (NotReviewed when nothing is recorded).</summary>
    [HttpGet]
    public async Task<ActionResult<SiteScreeningDto>> Get([FromQuery] string siteId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(siteId)) return BadRequest("siteId is required.");
        return await screening.GetAsync(siteId, ct);
    }

    /// <summary>Screening progress overall and per segment; optional topN limits to each segment's top-N ranks.</summary>
    [HttpGet("summary")]
    public Task<ScreeningSummaryDto> Summary([FromQuery] int? topN, CancellationToken ct) => screening.GetSummaryAsync(topN, ct);

    /// <summary>Set or update a site's decision. Promising / Needs more research / Unsuitable require evidence.</summary>
    [HttpPut]
    public async Task<ActionResult<SiteScreeningDto>> Upsert([FromBody] UpsertScreeningRequest req, CancellationToken ct)
    {
        if (!Enum.IsDefined(req.Decision)) return BadRequest("Unknown decision.");
        var (outcome, dto) = await screening.UpsertAsync(req, ct);
        return outcome switch
        {
            ScreeningService.UpsertOutcome.CandidateNotFound => NotFound($"No candidate with SiteId '{req.SiteId}'."),
            ScreeningService.UpsertOutcome.EvidenceRequired => BadRequest("Evidence is required: describe what you observed (satellite, Street View, records) that supports this decision."),
            ScreeningService.UpsertOutcome.EvidenceTooLong => BadRequest($"Evidence must be at most {ScreeningRules.MaxEvidenceLength} characters."),
            _ => Ok(dto)
        };
    }
}
