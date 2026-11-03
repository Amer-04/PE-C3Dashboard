using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace NJTruckCharging.Security;

/// <summary>
/// Guards destructive admin endpoints (workbook import / rescore). The app has no user login, so these
/// are protected separately:
/// <list type="bullet">
/// <item>In <b>Development</b> they are allowed (local use + tests).</item>
/// <item>In any other environment they are <b>disabled</b> unless an admin key is configured
/// (<c>Admin:ApiKey</c>) AND supplied in the <c>X-Admin-Key</c> request header.</item>
/// </list>
/// </summary>
public sealed class RequireAdminAccessAttribute : ActionFilterAttribute
{
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        var sp = context.HttpContext.RequestServices;
        var env = sp.GetRequiredService<IHostEnvironment>();
        if (env.IsDevelopment())
            return; // local development: allowed

        var configuredKey = sp.GetRequiredService<IConfiguration>()["Admin:ApiKey"];
        var provided = context.HttpContext.Request.Headers["X-Admin-Key"].ToString();
        if (!string.IsNullOrEmpty(configuredKey) && FixedEquals(configuredKey, provided))
            return; // separate admin mechanism satisfied

        context.Result = new ObjectResult(new ProblemDetails
        {
            Status = StatusCodes.Status403Forbidden,
            Title = "Administrative endpoints are disabled in this environment.",
            Detail = "Set Admin:ApiKey and send it in the X-Admin-Key header to enable them.",
        })
        { StatusCode = StatusCodes.Status403Forbidden };
    }

    private static bool FixedEquals(string a, string b)
    {
        var ab = Encoding.UTF8.GetBytes(a);
        var bb = Encoding.UTF8.GetBytes(b);
        return ab.Length == bb.Length && CryptographicOperations.FixedTimeEquals(ab, bb);
    }
}
