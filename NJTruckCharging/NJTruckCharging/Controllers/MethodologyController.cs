using Microsoft.AspNetCore.Mvc;
using NJTruckCharging.Application.Methodology;
using NJTruckCharging.Domain.Methodology;
using NJTruckCharging.Security;

namespace NJTruckCharging.Controllers;

/// <summary>
/// Editable scoring methodology. Reads are open (the dashboard shows the active methodology); every endpoint that
/// creates/edits drafts, writes proposed scores, publishes or restores requires the administrator key.
/// </summary>
[ApiController]
[Route("api/methodology")]
public class MethodologyController(MethodologyService svc) : ControllerBase
{
    [HttpGet("active")]
    public async Task<ActionResult<ActiveMethodologyDto>> Active(CancellationToken ct) =>
        await svc.GetActiveAsync(ct) is { } a ? a : Problem(statusCode: 503, title: "No active methodology (baseline not verified).");

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

    /// <summary>Validates a rule set without saving anything (used live by the editor).</summary>
    [HttpPost("validate")]
    public Task<ValidationResultDto> Validate([FromBody] MethodologyRules rules, CancellationToken ct) => svc.ValidateAsync(rules, ct);

    // ---- administrator-only -------------------------------------------------------------------------------------

    [HttpPost("drafts")]
    [RequireMethodologyAdmin]
    public async Task<ActionResult<VersionDetailDto>> CreateDraft([FromBody] CreateDraftRequest req, CancellationToken ct) =>
        Map(await svc.CreateDraftAsync(req, ct));

    [HttpPut("drafts/{id:int}")]
    [RequireMethodologyAdmin]
    public async Task<ActionResult<VersionDetailDto>> UpdateDraft(int id, [FromBody] UpdateDraftRequest req, CancellationToken ct) =>
        Map(await svc.UpdateDraftAsync(id, req, ct));

    [HttpDelete("drafts/{id:int}")]
    [RequireMethodologyAdmin]
    public async Task<ActionResult> DeleteDraft(int id, CancellationToken ct)
    {
        var r = await svc.DeleteDraftAsync(id, ct);
        return r.Error == MethodologyError.None ? NoContent() : Map(r).Result!;
    }

    /// <summary>Copies any version's rules into a new editable draft.</summary>
    [HttpPost("versions/{id:int}/duplicate")]
    [RequireMethodologyAdmin]
    public async Task<ActionResult<VersionDetailDto>> Duplicate(int id, [FromBody] CreateDraftRequest? req, CancellationToken ct) =>
        Map(await svc.CreateDraftAsync(new CreateDraftRequest(req?.Name, id), ct));

    /// <summary>Recalculates proposed scores and ranks for every site (stored separately; live scores untouched).</summary>
    [HttpPost("versions/{id:int}/preview")]
    [RequireMethodologyAdmin]
    public async Task<ActionResult<PreviewSummaryDto>> Preview(int id, int topN = 10, CancellationToken ct = default) =>
        Map(await svc.PreviewAsync(id, topN, ct));

    /// <summary>Atomically activates a previewed draft (publish) or a previous version (restore).</summary>
    [HttpPost("versions/{id:int}/publish")]
    [RequireMethodologyAdmin]
    public async Task<ActionResult<PublishResultDto>> Publish(int id, [FromBody] PublishRequest req, CancellationToken ct) =>
        Map(await svc.PublishAsync(id, req, ct));

    private ActionResult<T> Map<T>(MethodologyOutcome<T> r)
    {
        if (r.Error == MethodologyError.None) return r.Value!;
        var status = r.Error switch
        {
            MethodologyError.NotFound => 404,
            MethodologyError.Invalid => 400,
            MethodologyError.Forbidden => 403,
            _ => 409
        };
        var pd = new ProblemDetails { Status = status, Title = r.Message };
        if (r.Issues is { Count: > 0 }) pd.Extensions["issues"] = r.Issues;
        return new ObjectResult(pd) { StatusCode = status };
    }
}
