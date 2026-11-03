using Microsoft.AspNetCore.Mvc;
using NJTruckCharging.Application.Import;
using NJTruckCharging.Security;

namespace NJTruckCharging.Controllers;

[ApiController]
[Route("api/admin")]
[RequireAdminAccess] // import + rescore are destructive; disabled outside Development unless an admin key is supplied
public class AdminController(ImportService import) : ControllerBase
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
        return await import.ImportAsync(ms, ct);
    }

    /// <summary>Re-runs the scoring engine over the stored sites.</summary>
    [HttpPost("rescore")]
    public async Task<object> Rescore(CancellationToken ct) => new { rescored = await import.RescoreAsync(ct: ct) };
}
