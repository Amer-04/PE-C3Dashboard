using Microsoft.AspNetCore.Mvc;
using NJTruckCharging.Application.Dashboard;
using NJTruckCharging.Application.Sites;

namespace NJTruckCharging.Controllers;

[ApiController]
[Route("api/dashboard")]
public class DashboardController(DashboardService dashboard) : ControllerBase
{
    /// <summary>KPIs, per-segment statistics and distributions for the filtered sites.</summary>
    [HttpGet("summary")]
    public Task<DashboardSummaryDto> Summary([FromQuery] SiteFilter filter, CancellationToken ct) =>
        dashboard.GetSummaryAsync(filter, ct);
}
