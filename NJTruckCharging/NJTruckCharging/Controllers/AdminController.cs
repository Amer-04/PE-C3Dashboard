using Microsoft.AspNetCore.Mvc;
using NJTruckCharging.Application.Import;
using NJTruckCharging.Application.Methodology;
using NJTruckCharging.Domain.Entities;
using NJTruckCharging.Security;

namespace NJTruckCharging.Controllers;

[ApiController]
[Route("api/admin")]
[RequireAdminAccess] // import + rescore are destructive; disabled outside Development unless an admin key is supplied
public class AdminController(ImportService import, MethodologyService methodology) : ControllerBase
{
    /// <summary>Replaces all data with a new version of C3_candidate_sites_all_segments.xlsx.</summary>
    [HttpPost("import")]
    [RequestSizeLimit(200_000_000)]
    [RequestFormLimits(MultipartBodyLengthLimit = 200_000_000)]
    public async Task<ActionResult<ImportResultDto>> Import(IFormFile file, CancellationToken ct)
    {
        if (file.Length == 0 || !file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            return BadRequest("Upload the .xlsx workbook.");

        // OpenXml needs a seekable stream.
        await using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        ms.Position = 0;
        try
        {
            return await import.ImportAsync(ms, ct);
        }
        catch (Exception ex) when (ex is ImportValidationException or InvalidDataException)
        {
            return Problem(statusCode: 400, title: "Import rejected — nothing was changed.", detail: ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(statusCode: 409, title: "Import not started.", detail: ex.Message);
        }
    }

    /// <summary>Re-applies the ACTIVE methodology to the stored sites (atomic, same path as publishing).</summary>
    [HttpPost("rescore")]
    public async Task<ActionResult<object>> Rescore(CancellationToken ct)
    {
        var r = await methodology.ReapplyActiveAsync(ActivationReason.Rescore, ct);
        return r.Error == MethodologyError.None
            ? new { rescored = r.Value!.SitesUpdated, methodologyVersionId = r.Value.ActiveVersionId }
            : Problem(statusCode: 409, title: r.Message);
    }
}
