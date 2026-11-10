using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace NJTruckCharging.Security;

/// <summary>
/// Protects endpoints that change the scoring methodology (draft edits, previews that store proposed scores,
/// publish, restore) for a trusted INTERNAL deployment — with no credential in the browser:
/// <list type="number">
/// <item><b>Network:</b> the TCP peer address must be inside <c>Methodology:TrustedNetworks</c> (CIDR list;
/// default loopback only — the dashboard host / its dev proxy). <c>X-Forwarded-For</c> is never trusted; behind a
/// reverse proxy, list the proxy's address and restrict who can reach the proxy.</item>
/// <item><b>Cross-site requests:</b> a browser request must come from the dashboard itself — an <c>Origin</c> header,
/// when present, must be one of <c>Cors:Origins</c> or the API's own host, and <c>Sec-Fetch-Site: cross-site</c>
/// is rejected — so a web page elsewhere cannot drive a publish through a trusted user's browser.</item>
/// </list>
/// The typed confirmation in the dashboard is a deliberate-action safeguard only; this filter is the security control.
/// </summary>
public sealed class RequireMethodologyAdminAttribute : ActionFilterAttribute
{
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        var http = context.HttpContext;
        var config = http.RequestServices.GetRequiredService<IConfiguration>();

        var remote = http.Connection.RemoteIpAddress;
        var networks = config.GetSection("Methodology:TrustedNetworks").Get<string[]>() is { Length: > 0 } configured
            ? configured
            : ["127.0.0.0/8", "::1/128"];
        if (remote is null || !networks.Any(n => InNetwork(remote, n)))
        {
            context.Result = Forbid("Methodology changes are only accepted from the trusted internal network.",
                "Add this client's network to Methodology:TrustedNetworks on the server to allow it.");
            return;
        }

        var origin = http.Request.Headers.Origin.ToString();
        if (!string.IsNullOrEmpty(origin))
        {
            var allowed = config.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:4200"];
            var self = $"{http.Request.Scheme}://{http.Request.Host}";
            if (!allowed.Contains(origin, StringComparer.OrdinalIgnoreCase) && !string.Equals(origin, self, StringComparison.OrdinalIgnoreCase))
            {
                context.Result = Forbid("Cross-site methodology changes are not allowed.", null);
                return;
            }
        }
        if (string.Equals(http.Request.Headers["Sec-Fetch-Site"].ToString(), "cross-site", StringComparison.OrdinalIgnoreCase))
            context.Result = Forbid("Cross-site methodology changes are not allowed.", null);
    }

    private static ObjectResult Forbid(string title, string? detail) =>
        new(new ProblemDetails { Status = StatusCodes.Status403Forbidden, Title = title, Detail = detail })
        { StatusCode = StatusCodes.Status403Forbidden };

    /// <summary>CIDR match ("10.0.0.0/8", "::1/128"); IPv4-mapped IPv6 addresses are compared as IPv4.</summary>
    public static bool InNetwork(IPAddress address, string cidr)
    {
        var parts = cidr.Split('/', 2);
        if (!IPAddress.TryParse(parts[0].Trim(), out var network)) return false;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (network.IsIPv4MappedToIPv6) network = network.MapToIPv4();
        if (address.AddressFamily != network.AddressFamily) return false;
        var bits = parts.Length == 2 && int.TryParse(parts[1], out var b) ? b : network.GetAddressBytes().Length * 8;
        var a = address.GetAddressBytes();
        var n = network.GetAddressBytes();
        for (var i = 0; i < a.Length && bits > 0; i++, bits -= 8)
        {
            var mask = bits >= 8 ? 0xFF : (byte)(0xFF << (8 - bits));
            if ((a[i] & mask) != (n[i] & mask)) return false;
        }
        return true;
    }
}
