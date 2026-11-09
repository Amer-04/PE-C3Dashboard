using Microsoft.AspNetCore.Mvc;
using NJTruckCharging.Application.Methodology;
using NJTruckCharging.Security;

namespace NJTruckCharging.Controllers;

/// <summary>
/// User-defined custom data fields for methodology criteria. Reads are open; anything that creates fields or changes
/// values (which feed scoring at the next preview/publish) uses the same trusted-network + same-origin protection as
/// methodology changes.
/// </summary>
[ApiController]
[Route("api/custom-fields")]
public class CustomFieldsController(CustomFieldService svc) : ControllerBase
{
    [HttpGet]
    public Task<List<CustomFieldDto>> List(CancellationToken ct) => svc.ListAsync(ct);

    [HttpPost]
    [RequireMethodologyAdmin]
    public async Task<ActionResult<CustomFieldDto>> Create([FromBody] CustomFieldRequest req, CancellationToken ct) =>
        Map(await svc.CreateAsync(req, ct));

    [HttpPut("{key}")]
    [RequireMethodologyAdmin]
    public async Task<ActionResult<CustomFieldDto>> Update(string key, [FromBody] CustomFieldRequest req, CancellationToken ct) =>
        Map(await svc.UpdateAsync(key, req, ct));

    /// <summary>Every custom field for one site: its recorded value with evidence, or status "Unknown".</summary>
    [HttpGet("sites/{siteId}")]
    public Task<List<CustomValueDto>> ForSite(string siteId, CancellationToken ct) => svc.ValuesForSiteAsync(siteId, ct);

    [HttpPut("{key}/sites/{siteId}")]
    [RequireMethodologyAdmin]
    public async Task<ActionResult<CustomValueDto>> SetValue(string key, string siteId, [FromBody] SetCustomValueRequest req, CancellationToken ct) =>
        Map(await svc.SetManualAsync(key, siteId, req, ct));

    [HttpDelete("{key}/sites/{siteId}")]
    [RequireMethodologyAdmin]
    public async Task<ActionResult> ClearValue(string key, string siteId, CancellationToken ct)
    {
        var r = await svc.ClearAsync(key, siteId, ct);
        return r.Error == MethodologyError.None ? NoContent() : Map(r).Result!;
    }

    /// <summary>Validates a CSV (site_id, value[, evidence]) against the field and the candidate Site IDs; changes nothing.</summary>
    [HttpPost("{key}/import/preview")]
    [RequireMethodologyAdmin]
    [RequestSizeLimit(6_000_000)]
    public async Task<ActionResult<ImportPreviewDto>> PreviewImport(string key, [FromBody] ImportRequest req, CancellationToken ct) =>
        Map(await svc.PreviewImportAsync(key, req, ct));

    /// <summary>Commits the previewed CSV (same file + token, zero errors) in one transaction.</summary>
    [HttpPost("{key}/import/commit")]
    [RequireMethodologyAdmin]
    [RequestSizeLimit(6_000_000)]
    public async Task<ActionResult<ImportResultDto2>> CommitImport(string key, [FromBody] ImportRequest req, CancellationToken ct) =>
        Map(await svc.CommitImportAsync(key, req, ct));

    private ActionResult<T> Map<T>(MethodologyOutcome<T> r)
    {
        if (r.Error == MethodologyError.None) return r.Value!;
        var status = r.Error switch { MethodologyError.NotFound => 404, MethodologyError.Invalid => 400, MethodologyError.Forbidden => 403, _ => 409 };
        return new ObjectResult(new ProblemDetails { Status = status, Title = r.Message }) { StatusCode = status };
    }
}
