using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using NJTruckCharging.Application.Methodology;
using NJTruckCharging.Infrastructure;

namespace NJTruckCharging.Security;

/// <summary>
/// Shared, team-wide methodology editing.
/// <list type="bullet">
/// <item><b>Who:</b> with Supabase sign-in, every user the API already admits (verified e-mail, active account and session,
/// the configured access rule incl. administrator exceptions, not blocked) — checked again on every edit request, server
/// side. Without sign-in (office/local deployment) the original rule applies: requests from the trusted internal network.</item>
/// <item><b>Read-only mode:</b> stays on. When <c>Methodology:CloudEditing:Enabled=true</c>, exactly the endpoints marked
/// with <see cref="RequireMethodologyEditorAttribute"/> are let through it; every other change is still refused.</item>
/// <item><b>Database:</b> in the cloud those endpoints write through <c>ConnectionStrings:MethodologyWriter</c>, a role
/// that may only change methodology tables and the score columns of CandidateSites. Everything else keeps the
/// read-only login.</item>
/// </list>
/// </summary>
public static class MethodologyEditing
{
    public const string EnabledKey = "Methodology:CloudEditing:Enabled";
    public const string ScoringRateLimitPolicy = "methodology-scoring";
    public const string EditRateLimitPolicy = "methodology-edit";
    private const string ActorItem = "c3:methodology-actor";

    /// <summary>Only the exact value "true" (any case) turns editing on; empty, missing or anything else means off.</summary>
    public static bool IsCloudEditingEnabled(IConfiguration config) =>
        string.Equals(config[EnabledKey]?.Trim(), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Fail-closed start-up check. Cloud editing on PostgreSQL needs Supabase sign-in and a SEPARATE writer login; the
    /// read-only login must never be reused for writes, and the writer login must never serve ordinary requests.
    /// </summary>
    public static void ValidateConfiguration(IConfiguration config)
    {
        var writer = config.GetConnectionString(DependencyInjection.MethodologyWriterConnectionName);
        if (!IsCloudEditingEnabled(config) || !DatabaseProvider.IsPostgres(config)) return;
        if (!SupabaseAuth.IsEnabled(config))
            throw new InvalidOperationException($"{EnabledKey}=true requires Auth:Mode=Supabase.");
        if (string.IsNullOrWhiteSpace(writer))
            throw new InvalidOperationException(
                $"{EnabledKey}=true requires ConnectionStrings:{DependencyInjection.MethodologyWriterConnectionName} (the restricted methodology writer login).");
        var readUser = new Npgsql.NpgsqlConnectionStringBuilder(config.GetConnectionString("DefaultConnection")).Username;
        var writeUser = new Npgsql.NpgsqlConnectionStringBuilder(writer).Username;
        if (string.IsNullOrEmpty(writeUser) || string.Equals(readUser, writeUser, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"ConnectionStrings:{DependencyInjection.MethodologyWriterConnectionName} must use its own login (the methodology writer role), not the read-only login.");

        // Browser edits are accepted only from these EXACT origins (the dashboard), so they must be precise.
        var origins = config.GetSection("Cors:Origins").Get<string[]>() ?? [];
        if (origins.Length == 0)
            throw new InvalidOperationException($"{EnabledKey}=true requires Cors:Origins (the exact dashboard origin, e.g. https://c3dashboard.netlify.app).");
        foreach (var o in origins)
            if (!IsExactHttpsOrigin(o))
                throw new InvalidOperationException(
                    $"Cors:Origins entry '{o}' must be an exact https origin (scheme://host[:port], no wildcard, path, query or trailing slash).");
    }

    /// <summary>True for "https://host" or "https://host:port" exactly as a browser sends it in the Origin header.</summary>
    public static bool IsExactHttpsOrigin(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Contains('*') || value != value.Trim()) return false;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps) return false;
        if (!string.IsNullOrEmpty(u.UserInfo) || !string.IsNullOrEmpty(u.Query) || !string.IsNullOrEmpty(u.Fragment)) return false;
        var canonical = u.IsDefaultPort ? $"{u.Scheme}://{u.Host}" : $"{u.Scheme}://{u.Host}:{u.Port}";
        return string.Equals(canonical, value, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Decides whether a methodology change request may come from where it says it comes from (defence in depth; the
    /// bearer token is the real control and is never sent automatically by a browser).
    /// <list type="bullet">
    /// <item>Origin present: it must EXACTLY equal a trusted origin (Cors:Origins) or the API's own origin. Then the request
    /// is accepted whatever Sec-Fetch-Site says - the dashboard (netlify.app) and the API (onrender.com) are different
    /// sites, so a browser always marks the dashboard's legitimate requests "cross-site". Any other origin, including
    /// "null", lookalikes, http:// variants and deploy previews, is refused.</item>
    /// <item>No Origin: a browser request marked cross-site/same-site without its origin is refused; a request without
    /// Fetch-Metadata (curl, server-to-server) is allowed - authentication still decides.</item>
    /// </list>
    /// </summary>
    public static (bool Allowed, string Reason) EvaluateRequestSource(string? origin, string? secFetchSite, IReadOnlyCollection<string> trustedOrigins, string selfOrigin)
    {
        if (!string.IsNullOrEmpty(origin))
        {
            var trusted = trustedOrigins.Any(t => string.Equals(t, origin, StringComparison.OrdinalIgnoreCase))
                          || string.Equals(origin, selfOrigin, StringComparison.OrdinalIgnoreCase);
            return trusted ? (true, "trusted origin") : (false, "untrusted origin");
        }
        var site = (secFetchSite ?? "").Trim().ToLowerInvariant();
        return site is "cross-site" or "same-site" ? (false, "browser request without an origin") : (true, "no browser origin");
    }

    /// <summary>The read-only gate lets a request through only if editing is on and it targets a marked editor endpoint.</summary>
    public static bool AllowedInReadOnlyMode(HttpContext ctx, bool cloudEditingEnabled) =>
        cloudEditingEnabled && ctx.GetEndpoint()?.Metadata.GetMetadata<RequireMethodologyEditorAttribute>() is not null;

    /// <summary>The editor established by <see cref="RequireMethodologyEditorAttribute"/> for this request.</summary>
    public static MethodologyActor Actor(HttpContext ctx) =>
        ctx.Items[ActorItem] as MethodologyActor ?? throw new InvalidOperationException("No methodology editor on this request.");

    internal static void SetActor(HttpContext ctx, MethodologyActor actor) => ctx.Items[ActorItem] = actor;

    /// <summary>Rate-limit partition: the Supabase user id, or the client address for unsigned local requests.</summary>
    public static string Partition(HttpContext ctx) =>
        ctx.User.FindFirstValue("sub") ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}

/// <summary>
/// Marks (and protects) an endpoint that changes the shared methodology. Runs after authentication and the fallback
/// authorization policy, and checks again here so that a misconfigured route can never skip it.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RequireMethodologyEditorAttribute : Attribute, IAsyncActionFilter, IOrderedFilter
{
    /// <summary>
    /// Runs before every other action filter - in particular before [ApiController]'s automatic model validation
    /// (order -2000) - so an untrusted caller is refused before its request body is even validated.
    /// </summary>
    public int Order => int.MinValue + 1000;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var http = context.HttpContext;
        var config = http.RequestServices.GetRequiredService<IConfiguration>();
        if (!SupabaseAuth.IsEnabled(config))
        {
            // Office/local deployment without sign-in: unchanged rule (trusted network + same-origin requests).
            new RequireMethodologyAdminAttribute().OnActionExecuting(context);
            if (context.Result is not null) return;
            MethodologyEditing.SetActor(http, MethodologyActor.System);
            await next();
            return;
        }

        var user = http.User;
        var authz = http.RequestServices.GetRequiredService<IAuthorizationService>();
        var verified = user.Claims.Where(c => c.Type == SupabaseAuth.VerifiedEmailClaim).ToList();
        if (user.Identity?.IsAuthenticated != true
            || !Guid.TryParse(user.FindFirstValue("sub"), out var id)
            || verified.Count != 1 || verified[0].Issuer != SupabaseAuth.CheckedClaimsIssuer
            || !(await authz.AuthorizeAsync(user, null, new EmployeeAccessRequirement())).Succeeded)
        {
            context.Result = new ObjectResult(new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "This account cannot change the scoring methodology.",
            }) { StatusCode = StatusCodes.Status403Forbidden };
            return;
        }
        if (CrossSite(http, config))
        {
            context.Result = new ObjectResult(new ProblemDetails { Status = 403, Title = "Cross-site methodology changes are not allowed." }) { StatusCode = 403 };
            return;
        }
        MethodologyEditing.SetActor(http, new MethodologyActor(id.ToString(), verified[0].Value));
        await next();
    }

    /// <summary>A browser request must come from an exactly trusted origin (see <see cref="MethodologyEditing.EvaluateRequestSource"/>).</summary>
    private static bool CrossSite(HttpContext http, IConfiguration config)
    {
        var trusted = config.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:4200"];
        var self = $"{http.Request.Scheme}://{http.Request.Host}";
        return !MethodologyEditing.EvaluateRequestSource(
            http.Request.Headers.Origin.ToString(), http.Request.Headers["Sec-Fetch-Site"].ToString(), trusted, self).Allowed;
    }
}
