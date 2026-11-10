using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;

namespace NJTruckCharging.Security;

/// <summary>
/// Supabase Auth sign-in for the cloud deployment (<c>Auth:Mode = Supabase</c>).
/// <list type="bullet">
/// <item>Every request needs a Supabase access token (JWT) whose signature, issuer, audience and expiry are checked
/// here, on the server — 401 when it is missing or invalid.</item>
/// <item>The token's user id (<c>sub</c>) must be in the server-controlled allowlist <c>Auth:AllowedUserIds</c>
/// — 403 otherwise. The e-mail address and anything the browser says are never trusted for access.</item>
/// </list>
/// Signing keys come from the project's JWKS endpoint (asymmetric keys, the Supabase default); a project still on
/// the legacy shared secret can set <c>Auth:Supabase:JwtSecret</c> instead (server-side only).
/// </summary>
public static class SupabaseAuth
{
    public const string ModeKey = "Auth:Mode";
    public const string NotApprovedCode = "not_approved";

    public static bool IsEnabled(IConfiguration config) =>
        string.Equals(config[ModeKey], "Supabase", StringComparison.OrdinalIgnoreCase);

    /// <summary>Base URL of the Supabase project, e.g. https://abcd1234.supabase.co (no trailing slash).</summary>
    public static string ProjectUrl(IConfiguration config) =>
        (config["Auth:Supabase:Url"] ?? "").Trim().TrimEnd('/');

    public static string Issuer(IConfiguration config) => ProjectUrl(config) + "/auth/v1";

    /// <summary>Allowed Supabase user ids: a JSON array in appsettings, or one comma/space-separated environment value.</summary>
    public static HashSet<Guid> AllowedUserIds(IConfiguration config)
    {
        var section = config.GetSection("Auth:AllowedUserIds");
        var raw = section.GetChildren().Select(c => c.Value ?? "")
            .Concat((section.Value ?? "").Split([',', ';', ' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries));
        var ids = new HashSet<Guid>();
        foreach (var r in raw.Select(r => r.Trim()).Where(r => r.Length > 0))
            ids.Add(Guid.TryParse(r, out var g) ? g
                : throw new InvalidOperationException($"Auth:AllowedUserIds contains '{r}', which is not a Supabase user id (UUID)."));
        return ids;
    }

    public static IServiceCollection AddSupabaseAuth(this IServiceCollection services, IConfiguration config)
    {
        var url = ProjectUrl(config);
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps && !uri.IsLoopback)
            throw new InvalidOperationException("Auth:Supabase:Url must be the project's https URL, e.g. https://abcd1234.supabase.co");

        services.AddHttpClient(SupabaseSigningKeys.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(10));
        services.AddSingleton<SupabaseSigningKeys>();
        services.AddSingleton<IAuthorizationHandler, AllowlistedUserHandler>();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, ProblemAuthorizationResultHandler>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<SupabaseSigningKeys>((o, keys) =>
            {
                o.MapInboundClaims = false;                    // keep "sub", "role", "aud" exactly as Supabase issued them
                o.RequireHttpsMetadata = true;
                o.SaveToken = false;
                o.IncludeErrorDetails = false;                 // no validation details in WWW-Authenticate
                var symmetric = config["Auth:Supabase:JwtSecret"] is { Length: > 0 } secret
                    ? new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)) : null;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = Issuer(config),
                    ValidateAudience = true,
                    ValidAudience = config["Auth:Supabase:Audience"] is { Length: > 0 } aud ? aud : "authenticated",
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    RequireSignedTokens = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    ValidateIssuerSigningKey = true,
                    ValidAlgorithms = symmetric is null
                        ? [SecurityAlgorithms.EcdsaSha256, SecurityAlgorithms.RsaSha256]
                        : [SecurityAlgorithms.EcdsaSha256, SecurityAlgorithms.RsaSha256, SecurityAlgorithms.HmacSha256],
                    IssuerSigningKeyResolver = (_, _, kid, _) =>
                        symmetric is null ? keys.Resolve(kid) : keys.Resolve(kid).Append(symmetric),
                    NameClaimType = "email",
                    RoleClaimType = "role",
                };
            });

        services.AddAuthorization(o =>
        {
            // Every endpoint — data, CSV exports, map configuration — requires an approved user unless it explicitly
            // allows anonymous access (only the health check does).
            o.FallbackPolicy = new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .AddRequirements(new AllowlistedUserRequirement())
                .Build();
        });
        return services;
    }
}

/// <summary>Caches the Supabase project's public signing keys (JWKS); refreshes every 10 minutes or on an unknown key id.</summary>
public sealed class SupabaseSigningKeys(IHttpClientFactory http, IConfiguration config, ILogger<SupabaseSigningKeys> logger)
{
    public const string HttpClientName = "supabase-jwks";
    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan MinRefreshInterval = TimeSpan.FromSeconds(30);
    private readonly object _gate = new();
    private IReadOnlyList<SecurityKey> _keys = [];
    private DateTime _fetchedAt = DateTime.MinValue;
    private DateTime _lastAttempt = DateTime.MinValue;

    /// <summary>Test hook: fixed keys instead of fetching the JWKS.</summary>
    public IReadOnlyList<SecurityKey>? FixedKeys { get; set; }

    public IEnumerable<SecurityKey> Resolve(string? kid)
    {
        if (FixedKeys is { } fixedKeys) return Match(fixedKeys, kid);
        var keys = _keys;
        var unknownKid = kid is not null && !keys.Any(k => k.KeyId == kid);
        if (DateTime.UtcNow - _fetchedAt > MaxAge || unknownKid) keys = Refresh(force: unknownKid);
        return Match(keys, kid);
    }

    private static IEnumerable<SecurityKey> Match(IReadOnlyList<SecurityKey> keys, string? kid) =>
        kid is null ? keys : keys.Where(k => k.KeyId == kid);

    private IReadOnlyList<SecurityKey> Refresh(bool force)
    {
        lock (_gate)
        {
            if (DateTime.UtcNow - _lastAttempt < MinRefreshInterval && (!force || _keys.Count > 0)) return _keys;
            _lastAttempt = DateTime.UtcNow;
            try
            {
                var url = SupabaseAuth.Issuer(config) + "/.well-known/jwks.json";
                using var response = http.CreateClient(HttpClientName).Send(new HttpRequestMessage(HttpMethod.Get, url));
                response.EnsureSuccessStatusCode();
                using var reader = new StreamReader(response.Content.ReadAsStream());
                var set = new JsonWebKeySet(reader.ReadToEnd());
                _keys = set.GetSigningKeys().ToList();
                _fetchedAt = DateTime.UtcNow;
                logger.LogInformation("Loaded {Count} Supabase signing key(s).", _keys.Count);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not load the Supabase signing keys; keeping {Count} cached key(s).", _keys.Count);
            }
            return _keys;
        }
    }
}

public sealed class AllowlistedUserRequirement : IAuthorizationRequirement;

/// <summary>The signed-in Supabase user's id must be on the server's allowlist (read at startup from configuration).</summary>
public sealed class AllowlistedUserHandler(IConfiguration config) : AuthorizationHandler<AllowlistedUserRequirement>
{
    private readonly HashSet<Guid> _allowed = SupabaseAuth.AllowedUserIds(config);

    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, AllowlistedUserRequirement requirement)
    {
        var user = context.User;
        if (user.Identity?.IsAuthenticated == true
            && user.FindFirstValue("role") == "authenticated"
            && !string.Equals(user.FindFirstValue("is_anonymous"), "true", StringComparison.OrdinalIgnoreCase)
            && Guid.TryParse(user.FindFirstValue("sub"), out var id)
            && _allowed.Contains(id))
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}

/// <summary>401/403 with a short, plain-language problem body the sign-in screen can show.</summary>
public sealed class ProblemAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult result)
    {
        if (result.Forbidden)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Status = 403,
                Title = "Your account is not approved for C3 Site Finder. Ask the administrator to approve it.",
                Extensions = { ["code"] = SupabaseAuth.NotApprovedCode }
            }, options: null, contentType: "application/problem+json");
            return;
        }
        await _default.HandleAsync(next, context, policy, result);
    }
}
