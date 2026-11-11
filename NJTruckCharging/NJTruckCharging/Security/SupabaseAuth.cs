using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace NJTruckCharging.Security;

/// <summary>
/// Supabase Auth sign-in for the cloud deployment (<c>Auth:Mode = Supabase</c>). Every request (except /healthz) passes
/// three gates, all on the server:
/// <list type="number">
/// <item><b>Token</b>: a Supabase access token (JWT) whose signature, issuer, audience and expiry are valid, else 401.</item>
/// <item><b>Account</b>: Supabase itself confirms, right now, that the account and session still exist, the e-mail address
/// is confirmed (ownership proven) and the account is not banned (<see cref="SupabaseUserDirectory"/>), else 401
/// (503 if Supabase cannot be reached; never access).</item>
/// <item><b>Access rule</b> (<c>Auth:AccessRule</c>, see <see cref="AccessRule"/>): the confirmed e-mail domain is exactly a
/// company domain (<see cref="EmployeeEmailPolicy"/>) and/or the user id is on <c>Auth:AllowedUserIds</c>; never in
/// <c>Auth:BlockedUserIds</c>, else 403 <c>not_approved</c> with a generic message.</item>
/// </list>
/// Signing keys come from the project's JWKS endpoint (asymmetric keys, the Supabase default); a project still on
/// the legacy shared secret can set <c>Auth:Supabase:JwtSecret</c> instead (server-side only).
/// </summary>
public static class SupabaseAuth
{
    public const string ModeKey = "Auth:Mode";
    public const string NotApprovedCode = "not_approved";
    public const string UnavailableCode = "auth_unavailable";
    /// <summary>Issuer stamped on the claims this server adds after asking Supabase; a token can never carry them.</summary>
    public const string CheckedClaimsIssuer = "c3-site-finder:supabase-user-check";
    public const string VerifiedEmailClaim = "c3:verified_email";
    private const string UnavailableItem = "c3:auth-unavailable";

    public static bool IsEnabled(IConfiguration config) =>
        string.Equals(config[ModeKey], "Supabase", StringComparison.OrdinalIgnoreCase);

    /// <summary>Base URL of the Supabase project, e.g. https://abcd1234.supabase.co (no trailing slash).</summary>
    public static string ProjectUrl(IConfiguration config) =>
        (config["Auth:Supabase:Url"] ?? "").Trim().TrimEnd('/');

    public static string Issuer(IConfiguration config) => ProjectUrl(config) + "/auth/v1";

    /// <summary>Allowed Supabase user ids: a JSON array in appsettings, or one comma/space-separated environment value.</summary>
    public static HashSet<Guid> AllowedUserIds(IConfiguration config) => UserIds(config, "Auth:AllowedUserIds");

    /// <summary>User ids refused whatever else is true (immediate revocation without waiting for Supabase).</summary>
    public static HashSet<Guid> BlockedUserIds(IConfiguration config) => UserIds(config, "Auth:BlockedUserIds");

    private static HashSet<Guid> UserIds(IConfiguration config, string key)
    {
        var section = config.GetSection(key);
        var raw = section.GetChildren().Select(c => c.Value ?? "")
            .Concat((section.Value ?? "").Split([',', ';', ' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries));
        var ids = new HashSet<Guid>();
        foreach (var r in raw.Select(r => r.Trim()).Where(r => r.Length > 0))
            ids.Add(Guid.TryParse(r, out var g) ? g
                : throw new InvalidOperationException($"{key} contains '{r}', which is not a Supabase user id (UUID)."));
        return ids;
    }

    /// <summary>
    /// <c>Auth:AccessRule</c>. Unset = <see cref="AccessRule.UserIds"/> (the behaviour before self-registration), so deploying
    /// this code changes nothing until the rule is switched deliberately. Any other value is a startup error.
    /// </summary>
    public static AccessRule Rule(IConfiguration config)
    {
        var v = config["Auth:AccessRule"];
        if (string.IsNullOrWhiteSpace(v)) return AccessRule.UserIds;
        return Enum.TryParse<AccessRule>(v.Trim(), ignoreCase: false, out var r) && Enum.IsDefined(r) && !int.TryParse(v, out _) ? r
            : throw new InvalidOperationException(
                $"Auth:AccessRule '{v}' is not valid. Use UserIds, EmailDomainAndUserIds or EmailDomain.");
    }

    /// <summary>Refuses to start with a configuration that would let nobody in, or anybody in.</summary>
    public static void ValidateAccessConfiguration(IConfiguration config)
    {
        var rule = Rule(config);
        var allowed = AllowedUserIds(config);
        BlockedUserIds(config);                                            // throws on a malformed id
        if (rule != AccessRule.EmailDomain && allowed.Count == 0)
            throw new InvalidOperationException($"Auth:AccessRule={rule} needs Auth:AllowedUserIds: nobody could sign in. Add the approved Supabase user ids.");
        if (rule == AccessRule.EmailDomain && allowed.Count > 0)
            throw new InvalidOperationException("Auth:AccessRule=EmailDomain ignores Auth:AllowedUserIds. Remove Auth:AllowedUserIds " +
                "(or use EmailDomainAndUserIds) so the configuration says what it does.");
        var key = config["Auth:Supabase:PublishableKey"];
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("Auth:Supabase:PublishableKey is required: the API asks Supabase whether each account " +
                "is still active and its e-mail confirmed. Use the project's publishable (public) key, never a secret/service key.");
        if (key.StartsWith("sb_secret_", StringComparison.Ordinal))
            throw new InvalidOperationException("Auth:Supabase:PublishableKey holds a SECRET key. Use the publishable key.");
    }

    public static IServiceCollection AddSupabaseAuth(this IServiceCollection services, IConfiguration config)
    {
        var url = ProjectUrl(config);
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps && !uri.IsLoopback)
            throw new InvalidOperationException("Auth:Supabase:Url must be the project's https URL, e.g. https://abcd1234.supabase.co");

        ValidateAccessConfiguration(config);

        services.AddHttpClient(SupabaseSigningKeys.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(10));
        services.AddHttpClient(SupabaseUserDirectory.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(10));
        services.AddSingleton<SupabaseSigningKeys>();
        services.AddSingleton<ISupabaseUserDirectory, SupabaseUserDirectory>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IAuthorizationHandler, EmployeeAccessHandler>();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, ProblemAuthorizationResultHandler>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<SupabaseSigningKeys>((o, keys) =>
            {
                o.Events = new JwtBearerEvents
                {
                    OnTokenValidated = CheckAccountWithSupabaseAsync,
                    OnChallenge = WriteUnavailableIfSupabaseWasDownAsync,
                };
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
                .AddRequirements(new EmployeeAccessRequirement())
                .Build();
        });
        return services;
    }

    /// <summary>
    /// Runs only after the token's signature, issuer, audience and expiry were accepted. Asks Supabase about the account;
    /// a deleted/banned/unconfirmed account or a revoked session fails authentication (401). The e-mail address Supabase
    /// confirms is added as a claim under <see cref="CheckedClaimsIssuer"/>, which no token can forge.
    /// </summary>
    private static async Task CheckAccountWithSupabaseAsync(TokenValidatedContext context)
    {
        var principal = context.Principal;
        if (principal is null || context.SecurityToken is not Microsoft.IdentityModel.JsonWebTokens.JsonWebToken jwt)
        {
            context.Fail("No token.");
            return;
        }
        var directory = context.HttpContext.RequestServices.GetRequiredService<ISupabaseUserDirectory>();
        var account = await directory.LookupAsync(jwt.EncodedToken, context.HttpContext.RequestAborted);
        switch (account.Status)
        {
            case SupabaseUserStatus.Unavailable:
                context.HttpContext.Items[UnavailableItem] = true;
                context.Fail("Account check unavailable.");
                return;
            case SupabaseUserStatus.Revoked:
                context.Fail("Account or session no longer valid.");
                return;
        }
        if (!Guid.TryParse(principal.FindFirstValue("sub"), out var sub) || account.Id != sub
            || !account.EmailConfirmed || account.Banned || account.IsAnonymous || string.IsNullOrEmpty(account.Email))
        {
            context.Fail("Account not usable.");
            return;
        }
        principal.AddIdentity(new ClaimsIdentity(
            [new Claim(VerifiedEmailClaim, account.Email, ClaimValueTypes.String, CheckedClaimsIssuer)],
            authenticationType: CheckedClaimsIssuer));
    }

    /// <summary>Supabase unreachable: 503 (the dashboard keeps the session and says "try again"), not 401.</summary>
    private static async Task WriteUnavailableIfSupabaseWasDownAsync(JwtBearerChallengeContext context)
    {
        if (!context.HttpContext.Items.ContainsKey(UnavailableItem)) return;
        context.HandleResponse();
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        context.Response.Headers.RetryAfter = "30";
        await context.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = 503,
            Title = "Sign-in cannot be checked right now. Please try again in a few minutes.",
            Extensions = { ["code"] = UnavailableCode }
        }, options: null, contentType: "application/problem+json");
    }
}

/// <summary>Who may use the API once the token and the account have been checked.</summary>
public enum AccessRule
{
    /// <summary>Only user ids on <c>Auth:AllowedUserIds</c> (administrator-approved accounts; the original behaviour).</summary>
    UserIds,
    /// <summary>Transition: on <c>Auth:AllowedUserIds</c> AND a confirmed company e-mail address. Approved accounts with
    /// personal e-mail addresses lose access here, visibly, before self-registration is opened.</summary>
    EmailDomainAndUserIds,
    /// <summary>Self-registration: any account with a confirmed e-mail address at exactly a company domain.</summary>
    EmailDomain,
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

public sealed class EmployeeAccessRequirement : IAuthorizationRequirement;

/// <summary>
/// Applies <see cref="AccessRule"/> to a request whose token and account were already checked. The e-mail address used is
/// the one Supabase confirmed just now (claim issued by this server), and it must match the token's own e-mail claim.
/// </summary>
public sealed class EmployeeAccessHandler(IConfiguration config) : AuthorizationHandler<EmployeeAccessRequirement>
{
    private readonly AccessRule _rule = SupabaseAuth.Rule(config);
    private readonly HashSet<Guid> _allowed = SupabaseAuth.AllowedUserIds(config);
    private readonly HashSet<Guid> _blocked = SupabaseAuth.BlockedUserIds(config);

    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, EmployeeAccessRequirement requirement)
    {
        if (IsAllowed(context.User)) context.Succeed(requirement);
        return Task.CompletedTask;
    }

    public bool IsAllowed(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true
            || user.FindFirstValue("role") != "authenticated"
            || string.Equals(user.FindFirstValue("is_anonymous"), "true", StringComparison.OrdinalIgnoreCase)
            || !Guid.TryParse(user.FindFirstValue("sub"), out var id)
            || _blocked.Contains(id))
            return false;

        // Only this server adds the verified-e-mail claim: it must carry our issuer, and there must be exactly one.
        var verified = user.Claims.Where(c => c.Type == SupabaseAuth.VerifiedEmailClaim).ToList();
        if (verified.Count != 1 || verified[0].Issuer != SupabaseAuth.CheckedClaimsIssuer) return false;
        var email = verified[0].Value;

        var onList = _allowed.Contains(id);
        var companyEmail = EmployeeEmailPolicy.IsAllowed(email)
            && string.Equals(user.FindFirstValue("email"), email, StringComparison.OrdinalIgnoreCase);
        return _rule switch
        {
            AccessRule.UserIds => onList,
            AccessRule.EmailDomainAndUserIds => onList && companyEmail,
            AccessRule.EmailDomain => companyEmail,
            _ => false,
        };
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
                Title = "This account does not have access to C3 Site Finder.",
                Extensions = { ["code"] = SupabaseAuth.NotApprovedCode }
            }, options: null, contentType: "application/problem+json");
            return;
        }
        await _default.HandleAsync(next, context, policy, result);
    }
}
