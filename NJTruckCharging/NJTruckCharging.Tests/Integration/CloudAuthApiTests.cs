using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using NJTruckCharging.Security;

namespace NJTruckCharging.Tests.Integration;

/// <summary>
/// The app configured as the cloud demo: Supabase sign-in (JWT signature/issuer/audience/expiry checked on the server),
/// a server-side allowlist of Supabase user ids, and read-only mode. Tokens are signed with a test ES256 key that
/// stands in for the Supabase project's signing key.
/// </summary>
public sealed class CloudAuthWebAppFactory : TestWebAppFactory
{
    public const string ProjectUrl = "https://testproject.supabase.co";
    public static readonly Guid ApprovedUser = Guid.Parse("6f1d3c52-7a0e-4b8e-9d2a-1c5b8e7f9a01");
    public static readonly Guid UninvitedUser = Guid.Parse("0b9e4a77-2c41-4f6d-8e3b-5a7c9d1e2f03");

    public static ECDsaSecurityKey SigningKey { get; } = NewKey("supabase-test-key");

    protected override string DatabaseName => "NJTruckCharging_CloudAuthTest";

    public static ECDsaSecurityKey NewKey(string kid) => new(ECDsa.Create(ECCurve.NamedCurves.nistP256)) { KeyId = kid };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Auth:Mode", "Supabase");
        builder.UseSetting("Auth:Supabase:Url", ProjectUrl);
        builder.UseSetting("Auth:AllowedUserIds", ApprovedUser.ToString());
        builder.UseSetting("ReadOnlyDemo:Enabled", "true");
        builder.ConfigureServices(s => s.AddSingleton<IStartupFilter>(new KeysFilter()));
    }

    /// <summary>Installs the test signing key in place of the project's JWKS once the host is built.</summary>
    private sealed class KeysFilter : IStartupFilter
    {
        public Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> Configure(Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> next) => app =>
        {
            app.ApplicationServices.GetRequiredService<SupabaseSigningKeys>().FixedKeys = [SigningKey];
            next(app);
        };
    }

    /// <summary>A Supabase-shaped access token. Every argument can be bent to produce an invalid one.</summary>
    public static string Token(Guid? user = null, SecurityKey? key = null, string? issuer = null, string audience = "authenticated",
        DateTime? expires = null, string role = "authenticated", bool anonymous = false)
    {
        var now = DateTime.UtcNow;
        var exp = expires ?? now.AddMinutes(30);
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer ?? ProjectUrl + "/auth/v1",
            Audience = audience,
            IssuedAt = exp < now ? exp.AddHours(-1) : now,
            NotBefore = exp < now ? exp.AddHours(-1) : now,
            Expires = exp,
            Claims = new Dictionary<string, object>
            {
                ["sub"] = (user ?? ApprovedUser).ToString(),
                ["email"] = "someone@example.com",
                ["role"] = role,
                ["is_anonymous"] = anonymous,
            },
            SigningCredentials = new SigningCredentials(key ?? SigningKey,
                key is SymmetricSecurityKey ? SecurityAlgorithms.HmacSha256 : SecurityAlgorithms.EcdsaSha256),
        });
    }

    public HttpClient ClientWith(string? token)
    {
        var c = CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        if (token is not null) c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }
}

public class CloudAuthApiTests(CloudAuthWebAppFactory f) : IClassFixture<CloudAuthWebAppFactory>, IAsyncLifetime
{
    private static string T(Guid? user = null, SecurityKey? key = null, string? issuer = null, string audience = "authenticated",
        DateTime? expires = null, string role = "authenticated", bool anonymous = false) =>
        CloudAuthWebAppFactory.Token(user, key, issuer, audience, expires, role, anonymous);

    public Task InitializeAsync() => f.EnsureSeededAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>Business-data endpoints, including both CSV exports and the map configuration.</summary>
    public static TheoryData<string> DataEndpoints => new()
    {
        "/api/sites?pageSize=5",
        "/api/sites/map",
        "/api/sites/export",
        "/api/compare/export?siteIds=TEST-0001&siteIds=TEST-0002",
        "/api/dashboard/summary",
        "/api/methodology/active",
        "/api/saved-sites",
        "/api/config/maps",
        "/api/no-such-endpoint",
    };

    [Theory]
    [MemberData(nameof(DataEndpoints))]
    public async Task Requests_without_a_token_get_401(string url)
    {
        var r = await f.ClientWith(null).GetAsync(url);
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        Assert.DoesNotContain("TEST-0001", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_approved_user_can_read_data_and_download_csv()
    {
        var c = f.ClientWith(T());
        var sites = await c.GetAsync("/api/sites?pageSize=5");
        Assert.Equal(HttpStatusCode.OK, sites.StatusCode);
        Assert.Equal("no-store", sites.Headers.CacheControl?.ToString());

        var csv = await c.GetAsync("/api/sites/export");
        Assert.Equal(HttpStatusCode.OK, csv.StatusCode);
        Assert.Contains("TEST-0001", await csv.Content.ReadAsStringAsync());

        var cmp = await c.GetAsync("/api/compare/export?siteIds=TEST-0001&siteIds=TEST-0002");
        Assert.Equal(HttpStatusCode.OK, cmp.StatusCode);
    }

    public static TheoryData<string, string> BadTokens()
    {
        var other = CloudAuthWebAppFactory.NewKey("supabase-test-key");                  // same key id, wrong key
        return new()
        {
            { "expired", T(expires: DateTime.UtcNow.AddMinutes(-5)) },
            { "signed by another key", T(key: other) },
            { "unknown key id", T(key: CloudAuthWebAppFactory.NewKey("someone-else")) },
            { "HS256 with a guessed secret", T(key: new SymmetricSecurityKey(Encoding.UTF8.GetBytes("a-guessed-secret-that-is-long-enough-32b"))) },
            { "another Supabase project", T(issuer: "https://otherproject.supabase.co/auth/v1") },
            { "wrong audience", T(audience: "anon") },
            { "garbage", "not-a-jwt" },
            { "unsigned (alg none)", UnsignedToken() },
            { "tampered payload", Tamper(T()) },
        };
    }

    [Theory]
    [MemberData(nameof(BadTokens))]
    public async Task Invalid_tokens_get_401(string why, string token)
    {
        var r = await f.ClientWith(token).GetAsync("/api/sites?pageSize=5");
        Assert.True(r.StatusCode == HttpStatusCode.Unauthorized, $"{why}: expected 401, got {(int)r.StatusCode}");
    }

    [Fact]
    public async Task A_valid_token_for_an_uninvited_user_gets_403_not_approved()
    {
        var r = await f.ClientWith(T(user: CloudAuthWebAppFactory.UninvitedUser)).GetAsync("/api/sites?pageSize=5");
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        var body = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("not_approved", body.GetProperty("code").GetString());

        var csv = await f.ClientWith(T(user: CloudAuthWebAppFactory.UninvitedUser)).GetAsync("/api/sites/export");
        Assert.Equal(HttpStatusCode.Forbidden, csv.StatusCode);
    }

    [Fact]
    public async Task Email_is_never_used_for_access_and_non_user_roles_are_refused()
    {
        // The approved user's email on another account's token does not help; only the user id counts.
        Assert.Equal(HttpStatusCode.Forbidden, (await f.ClientWith(T(user: CloudAuthWebAppFactory.UninvitedUser)).GetAsync("/api/sites")).StatusCode);
        // Supabase anonymous sign-ins and service tokens are not employees.
        Assert.Equal(HttpStatusCode.Forbidden, (await f.ClientWith(T(anonymous: true)).GetAsync("/api/sites")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await f.ClientWith(T(role: "service_role")).GetAsync("/api/sites")).StatusCode);
    }

    [Fact]
    public async Task A_revoked_user_is_refused_once_removed_from_the_allowlist()
    {
        var token = T();
        Assert.Equal(HttpStatusCode.OK, (await f.ClientWith(token).GetAsync("/api/sites?pageSize=1")).StatusCode);

        // The administrator removes the user id from Auth:AllowedUserIds (and the host restarts with that setting).
        using var revoked = f.WithWebHostBuilder(b => b.UseSetting("Auth:AllowedUserIds", CloudAuthWebAppFactory.UninvitedUser.ToString()));
        var c = revoked.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/api/sites?pageSize=1")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/api/sites/export")).StatusCode);
    }

    public static TheoryData<string, string> Writes => new()
    {
        { "POST", "/api/saved-sites" },
        { "PUT", "/api/saved-sites/1" },
        { "DELETE", "/api/saved-sites/1" },
        { "POST", "/api/contacts" },
        { "PUT", "/api/contacts/1" },
        { "DELETE", "/api/contacts/1" },
        { "PUT", "/api/feasibility" },
        { "PUT", "/api/screening" },
        { "POST", "/api/research/start" },
        { "POST", "/api/custom-fields" },
        { "PUT", "/api/custom-fields/x/sites/TEST-0001" },
        { "POST", "/api/custom-fields/x/import/commit" },
        { "POST", "/api/methodology/drafts" },
        { "PUT", "/api/methodology/drafts/1" },
        { "POST", "/api/methodology/versions/1/preview" },
        { "POST", "/api/methodology/versions/1/publish" },
        { "POST", "/api/admin/import" },
        { "POST", "/api/admin/rescore" },
        { "PATCH", "/api/sites/TEST-0001" },
    };

    [Theory]
    [MemberData(nameof(Writes))]
    public async Task Every_change_is_refused_even_for_an_approved_user(string method, string url)
    {
        var req = new HttpRequestMessage(new HttpMethod(method), url) { Content = JsonContent.Create(new { siteId = "TEST-0001", confirm = true }) };
        var r = await f.ClientWith(T()).SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Equal("read_only", (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Theory]
    [MemberData(nameof(Writes))]
    public async Task Changes_without_a_token_get_401_first(string method, string url)
    {
        var r = await f.ClientWith(null).SendAsync(new HttpRequestMessage(new HttpMethod(method), url) { Content = JsonContent.Create(new { }) });
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task Read_only_mode_leaves_the_data_unchanged()
    {
        var c = f.ClientWith(T());
        var before = await (await c.GetAsync("/api/saved-sites")).Content.ReadAsStringAsync();
        await c.PostAsJsonAsync("/api/saved-sites", new { siteId = "TEST-0001" });
        await c.PostAsJsonAsync("/api/methodology/versions/1/publish", new { confirm = true });
        Assert.Equal(before, await (await c.GetAsync("/api/saved-sites")).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Health_check_is_public_and_reveals_nothing()
    {
        var r = await f.ClientWith(null).GetAsync("/healthz");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("ok", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Debug_and_OpenAPI_endpoints_are_not_exposed()
    {
        var c = f.ClientWith(T());
        Assert.NotEqual(HttpStatusCode.OK, (await c.GetAsync("/openapi/v1.json")).StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, (await f.ClientWith(null).GetAsync("/openapi/v1.json")).StatusCode);
    }

    [Fact]
    public async Task Cors_allows_only_the_configured_dashboard_origin_with_the_authorization_header()
    {
        var pre = new HttpRequestMessage(HttpMethod.Options, "/api/sites");
        pre.Headers.Add("Origin", "https://evil.example");
        pre.Headers.Add("Access-Control-Request-Method", "GET");
        pre.Headers.Add("Access-Control-Request-Headers", "authorization");
        var r = await f.ClientWith(null).SendAsync(pre);
        Assert.False(r.Headers.Contains("Access-Control-Allow-Origin"));

        var ok = new HttpRequestMessage(HttpMethod.Options, "/api/sites");
        ok.Headers.Add("Origin", "http://localhost:4200");
        ok.Headers.Add("Access-Control-Request-Method", "GET");
        ok.Headers.Add("Access-Control-Request-Headers", "authorization");
        var r2 = await f.ClientWith(null).SendAsync(ok);
        Assert.Equal("http://localhost:4200", r2.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }

    private static string UnsignedToken()
    {
        static string B64(string s) => Base64UrlEncoder.Encode(s);
        var payload = $$"""{"sub":"{{CloudAuthWebAppFactory.ApprovedUser}}","role":"authenticated","aud":"authenticated","iss":"{{CloudAuthWebAppFactory.ProjectUrl}}/auth/v1","exp":{{DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()}}}""";
        return $"{B64("""{"alg":"none","typ":"JWT"}""")}.{B64(payload)}.";
    }

    private static string Tamper(string jwt)
    {
        var parts = jwt.Split('.');
        var payload = Base64UrlEncoder.Decode(parts[1]).Replace(CloudAuthWebAppFactory.ApprovedUser.ToString(), "11111111-1111-1111-1111-111111111111");
        return $"{parts[0]}.{Base64UrlEncoder.Encode(payload)}.{parts[2]}";
    }
}
