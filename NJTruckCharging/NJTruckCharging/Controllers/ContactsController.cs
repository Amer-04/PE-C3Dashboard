using Microsoft.AspNetCore.Mvc;
using NJTruckCharging.Application.Contacts;

namespace NJTruckCharging.Controllers;

/// <summary>
/// Researched decision-maker contacts for a site (owner, broker, facility/sustainability/energy
/// manager, fleet rep …). Separate from nearby carriers, which are proximity signals, not confirmed
/// on-site contacts. Requires authentication (inherited global policy); the signed-in user is recorded
/// as researcher/verifier.
/// </summary>
[ApiController]
[Route("api/contacts")]
public class ContactsController(ContactService contacts) : ControllerBase
{
    private string? CurrentUser => User.Identity?.Name;

    /// <summary>All researched contacts for a site, verified first.</summary>
    [HttpGet]
    public async Task<ActionResult<List<SiteContactDto>>> List([FromQuery] string siteId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(siteId)) return BadRequest("siteId is required.");
        return await contacts.ListAsync(siteId, ct);
    }

    /// <summary>Per-site rollup (counts + the four-state distinction) for badges.</summary>
    [HttpGet("summary")]
    public async Task<ActionResult<SiteContactSummaryDto>> Summary([FromQuery] string siteId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(siteId)) return BadRequest("siteId is required.");
        return await contacts.GetSummaryAsync(siteId, ct);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<SiteContactDto>> Get(int id, CancellationToken ct) =>
        await contacts.GetByIdAsync(id, ct) is { } dto ? dto : NotFound();

    [HttpPost]
    public async Task<ActionResult<SiteContactDto>> Create([FromBody] CreateContactRequest req, CancellationToken ct)
    {
        var (outcome, dto) = await contacts.CreateAsync(req, CurrentUser, ct);
        return outcome switch
        {
            ContactService.CreateOutcome.CandidateNotFound => NotFound($"No candidate with SiteId '{req.SiteId}'."),
            ContactService.CreateOutcome.SourceRequired => BadRequest(SourceRequiredMessage),
            _ => CreatedAtAction(nameof(Get), new { id = dto!.Id }, dto),
        };
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<SiteContactDto>> Update(int id, [FromBody] UpdateContactRequest req, CancellationToken ct)
    {
        var (outcome, dto) = await contacts.UpdateAsync(id, req, CurrentUser, ct);
        return outcome switch
        {
            ContactService.UpdateOutcome.NotFound => NotFound(),
            ContactService.UpdateOutcome.SourceRequired => BadRequest(SourceRequiredMessage),
            _ => dto!
        };
    }

    private const string SourceRequiredMessage =
        "A source URL is required to mark a contact Verified (where the name, role and phone/email were published).";

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct) =>
        await contacts.DeleteAsync(id, ct) ? NoContent() : NotFound();
}
