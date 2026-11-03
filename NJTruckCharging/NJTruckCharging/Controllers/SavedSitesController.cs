using Microsoft.AspNetCore.Mvc;
using NJTruckCharging.Application.SavedSites;

namespace NJTruckCharging.Controllers;

/// <summary>
/// Bookmarked candidate sites — flagged during C3 discovery for closer evaluation. A simple
/// bookmark list (with an optional note), not a sales pipeline.
/// </summary>
[ApiController]
[Route("api/saved-sites")]
public class SavedSitesController(SavedSiteService saved) : ControllerBase
{
    /// <summary>Bookmarked sites, newest first, with candidate detail joined.</summary>
    [HttpGet]
    public Task<List<SavedSiteDto>> List(CancellationToken ct) => saved.ListAsync(ct);

    /// <summary>Compact SiteId → saved id markers, so the dashboard can flag bookmarked sites.</summary>
    [HttpGet("refs")]
    public Task<List<SavedSiteRef>> Refs(CancellationToken ct) => saved.ListRefsAsync(ct);

    [HttpGet("{id:int}")]
    public async Task<ActionResult<SavedSiteDto>> Get(int id, CancellationToken ct) =>
        await saved.GetByIdAsync(id, ct) is { } dto ? dto : NotFound();

    /// <summary>Bookmark a candidate. Idempotent on SiteId: re-saving returns the existing row (200).</summary>
    [HttpPost]
    public async Task<ActionResult<SavedSiteDto>> Save([FromBody] SaveSiteRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.SiteId))
            return BadRequest("SiteId is required.");

        var (outcome, dto) = await saved.SaveAsync(req, ct);
        return outcome switch
        {
            SavedSiteService.SaveOutcome.CandidateNotFound => NotFound($"No candidate with SiteId '{req.SiteId}'."),
            SavedSiteService.SaveOutcome.Created => CreatedAtAction(nameof(Get), new { id = dto!.Id }, dto),
            _ => Ok(dto) // AlreadySaved
        };
    }

    /// <summary>Edit the note on a bookmark.</summary>
    [HttpPut("{id:int}")]
    public async Task<ActionResult<SavedSiteDto>> Update(int id, [FromBody] UpdateSavedSiteRequest req, CancellationToken ct) =>
        await saved.UpdateAsync(id, req, ct) is { } dto ? dto : NotFound();

    /// <summary>Remove the bookmark.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct) =>
        await saved.DeleteAsync(id, ct) ? NoContent() : NotFound();
}
