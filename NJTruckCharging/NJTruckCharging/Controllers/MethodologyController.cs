using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NJTruckCharging.Application.Methodology;
using NJTruckCharging.Domain.Methodology;
using NJTruckCharging.Security;

namespace NJTruckCharging.Controllers;

/// <summary>
/// The shared, team-wide scoring methodology. Reads are open to every signed-in user. Every endpoint that validates,
/// creates/edits/deletes drafts, stores proposed scores, publishes or restores is marked
/// <see cref="RequireMethodologyEditorAttribute"/>: the editor is checked server side on each request, the request is
/// audited with the editor's Supabase user id and verified e-mail, and expensive recalculations are rate limited.
/// </summary>
[ApiController]
[Route("api/methodology")]
public class MethodologyController(MethodologyService svc, IConfiguration config) : ControllerBase
{
    [HttpGet("active")]
    public async Task<ActionResult<ActiveMethodologyDto>> Active(CancellationToken ct) =>
        await svc.GetActiveAsync(ct) is { } a ? a : Problem(statusCode: 503, title: "No active methodology (baseline not verified).");

    /// <summary>Whether the dashboard should offer editing on this deployment (the edit endpoints decide for themselves).</summary>
    [HttpGet("editing")]
    public EditingStatusDto Editing() =>
        config.GetValue<bool>("ReadOnlyDemo:Enabled") && !MethodologyEditing.IsCloudEditingEnabled(config)
            ? new EditingStatusDto(false, "Scoring rules are read-only on this deployment.")
            : new EditingStatusDto(true, null);

    [HttpGet("fields")]
    public async Task<IReadOnlyList<FieldDto>> Fields(CancellationToken ct) => await svc.GetFieldsAsync(ct);

    [HttpGet("versions")]
    public Task<List<VersionSummaryDto>> Versions(CancellationToken ct) => svc.ListVersionsAsync(ct);

    [HttpGet("versions/{id:int}")]
    public async Task<ActionResult<VersionDetailDto>> Version(int id, CancellationToken ct) =>
        await svc.GetVersionAsync(id, ct) is { } v ? v : NotFound();

    [HttpGet("versions/{id:int}/comparison")]
    public async Task<ActionResult<ComparisonPageDto>> Comparison(int id, int? segment, string? sort, string? search,
        int page = 1, int pageSize = 50, CancellationToken ct = default) =>
        await svc.GetComparisonAsync(id, segment, sort, search, page, pageSize, ct) is { } c ? c : NotFound();

    [HttpGet("activations")]
    public Task<List<ActivationDto>> Activations(CancellationToken ct) => svc.GetActivationsAsync(ct);

    /// <summary>Audit trail of every methodology change (who, when, previous → new values), newest first.</summary>
    [HttpGet("changes")]
    public Task<ChangeLogPageDto> Changes(int? versionId, int page = 1, int pageSize = 25, CancellationToken ct = default) =>
        svc.GetChangesAsync(versionId, page, pageSize, ct);

    /// <summary>Full previous and new state (name, summary, all rules) recorded for one audited save or deletion.</summary>
    [HttpGet("changes/{id:long}/snapshot")]
    public async Task<ActionResult<ChangeSnapshotDto>> ChangeSnapshot(long id, CancellationToken ct) =>
        await svc.GetChangeSnapshotAsync(id, ct) is { } s ? s : NotFound();

    // ---- editing (any approved C3 user; see RequireMethodologyEditorAttribute) ----------------------------------

    /// <summary>Validates a rule set without saving anything (used live by the editor).</summary>
    [HttpPost("validate")]
    [RequireMethodologyEditor, EnableRateLimiting(MethodologyEditing.EditRateLimitPolicy)]
    public Task<ValidationResultDto> Validate([FromBody] MethodologyRules rules, CancellationToken ct) => svc.ValidateAsync(rules, ct);

    [HttpPost("drafts")]
    [RequireMethodologyEditor, EnableRateLimiting(MethodologyEditing.EditRateLimitPolicy)]
    public async Task<ActionResult<VersionDetailDto>> CreateDraft([FromBody] CreateDraftRequest req, CancellationToken ct) =>
        Map(await svc.CreateDraftAsync(req, MethodologyEditing.Actor(HttpContext), ct));

    /// <summary>Saves a draft. <c>expectedRevision</c> must be the revision the edit started from (else 409 draft_conflict).</summary>
    [HttpPut("drafts/{id:int}")]
    [RequireMethodologyEditor, EnableRateLimiting(MethodologyEditing.EditRateLimitPolicy)]
    public async Task<ActionResult<VersionDetailDto>> UpdateDraft(int id, [FromBody] UpdateDraftRequest req, CancellationToken ct) =>
        Map(await svc.UpdateDraftAsync(id, req, MethodologyEditing.Actor(HttpContext), ct));

    [HttpDelete("drafts/{id:int}")]
    [RequireMethodologyEditor, EnableRateLimiting(MethodologyEditing.EditRateLimitPolicy)]
    public async Task<ActionResult> DeleteDraft(int id, int? revision, CancellationToken ct)
    {
        var r = await svc.DeleteDraftAsync(id, revision, MethodologyEditing.Actor(HttpContext), ct);
        return r.Error == MethodologyError.None ? NoContent() : Map(r).Result!;
    }

    /// <summary>Copies any version's rules into a new editable draft.</summary>
    [HttpPost("versions/{id:int}/duplicate")]
    [RequireMethodologyEditor, EnableRateLimiting(MethodologyEditing.EditRateLimitPolicy)]
    public async Task<ActionResult<VersionDetailDto>> Duplicate(int id, [FromBody] CreateDraftRequest? req, CancellationToken ct) =>
        Map(await svc.CreateDraftAsync(new CreateDraftRequest(req?.Name, id), MethodologyEditing.Actor(HttpContext), ct));

    /// <summary>Recalculates proposed scores and ranks for every site (stored separately; live scores untouched).</summary>
    [HttpPost("versions/{id:int}/preview")]
    [RequireMethodologyEditor, EnableRateLimiting(MethodologyEditing.ScoringRateLimitPolicy)]
    public async Task<ActionResult<PreviewSummaryDto>> Preview(int id, int topN = 10, CancellationToken ct = default) =>
        Map(await svc.PreviewAsync(id, topN, MethodologyEditing.Actor(HttpContext), ct));

    /// <summary>Atomically activates a previewed draft (publish) or a previous version (restore) for the whole team.</summary>
    [HttpPost("versions/{id:int}/publish")]
    [RequireMethodologyEditor, EnableRateLimiting(MethodologyEditing.ScoringRateLimitPolicy)]
    public async Task<ActionResult<PublishResultDto>> Publish(int id, [FromBody] PublishRequest req, CancellationToken ct) =>
        Map(await svc.PublishAsync(id, req, MethodologyEditing.Actor(HttpContext), ct));

    private ActionResult<T> Map<T>(MethodologyOutcome<T> r)
    {
        if (r.Error == MethodologyError.None) return r.Value!;
        var status = r.Code == MethodologyCodes.Failed ? 503 : r.Error switch
        {
            MethodologyError.NotFound => 404,
            MethodologyError.Invalid => 400,
            MethodologyError.Forbidden => 403,
            _ => 409
        };
        var pd = new ProblemDetails { Status = status, Title = r.Message };
        if (r.Code is not null) pd.Extensions["code"] = r.Code;
        if (r.Issues is { Count: > 0 }) pd.Extensions["issues"] = r.Issues;
        return new ObjectResult(pd) { StatusCode = status };
    }
}
