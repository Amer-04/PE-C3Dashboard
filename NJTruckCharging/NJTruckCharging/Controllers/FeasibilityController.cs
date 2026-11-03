using Microsoft.AspNetCore.Mvc;
using NJTruckCharging.Application.Feasibility;

namespace NJTruckCharging.Controllers;

/// <summary>
/// Site-feasibility checklist (truck access, maneuvering, usable land, parking, highway access,
/// electrical, flood/environmental, zoning, ownership/lease, owner interest). A ground/records
/// verification, kept entirely separate from the C3 score. Requires authentication; the signed-in
/// user is recorded as the reviewer.
/// </summary>
[ApiController]
[Route("api/feasibility")]
public class FeasibilityController(FeasibilityService feasibility) : ControllerBase
{
    private string? CurrentUser => User.Identity?.Name;

    /// <summary>All checklist items for a site (Unknown where not yet reviewed).</summary>
    [HttpGet]
    public async Task<ActionResult<List<FeasibilityItemDto>>> Checklist([FromQuery] string siteId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(siteId)) return BadRequest("siteId is required.");
        return await feasibility.GetChecklistAsync(siteId, ct);
    }

    /// <summary>Progress rollup (verified / concern / unknown counts).</summary>
    [HttpGet("summary")]
    public async Task<ActionResult<FeasibilitySummaryDto>> Summary([FromQuery] string siteId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(siteId)) return BadRequest("siteId is required.");
        return await feasibility.GetSummaryAsync(siteId, ct);
    }

    /// <summary>Create or update one checklist item's verdict + evidence.</summary>
    [HttpPut]
    public async Task<ActionResult<FeasibilityItemDto>> Upsert([FromBody] UpsertFeasibilityRequest req, CancellationToken ct)
    {
        var (outcome, dto) = await feasibility.UpsertAsync(req, CurrentUser, ct);
        return outcome switch
        {
            FeasibilityService.UpsertOutcome.CandidateNotFound => NotFound($"No candidate with SiteId '{req.SiteId}'."),
            FeasibilityService.UpsertOutcome.EvidenceRequired => BadRequest("Evidence is required for this verdict (what was checked and where — e.g. the tax-map or source link)."),
            _ => Ok(dto)
        };
    }
}
