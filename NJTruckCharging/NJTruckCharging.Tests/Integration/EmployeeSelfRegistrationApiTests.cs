using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace NJTruckCharging.Tests.Integration;

/// <summary>The cloud demo with self-registration: <c>Auth:AccessRule=EmailDomain</c>, no user-id allowlist.</summary>
public sealed class DomainAuthWebAppFactory : CloudAuthWebAppFactory
{
    protected override string DatabaseName => "NJTruckCharging_DomainAuthTest";

    protected override void ConfigureAccess(IWebHostBuilder builder) => builder.UseSetting("Auth:AccessRule", "EmailDomain");

    /// <summary>A client for a host started with different settings (same database, same fake Supabase users).</summary>
    public HttpClient ClientFor(WebApplicationFactory<Program> host, string token)
    {
        var c = host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }
}

public class EmployeeSelfRegistrationApiTests(DomainAuthWebAppFactory f) : IClassFixture<DomainAuthWebAppFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => f.EnsureSeededAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<HttpStatusCode> Get(string token, string url = "/api/sites?pageSize=1") =>
        (await f.ClientWith(token).GetAsync(url)).StatusCode;

    private static async Task<string?> Code(HttpResponseMessage r) =>
        (await r.Content.ReadFromJsonAsync<JsonElement>()).TryGetProperty("code", out var c) ? c.GetString() : null;

    [Theory]
    [InlineData("new.hire@poweredison.com")]
    [InlineData("new.hire@ev-edison.com")]
    [InlineData("Mixed.Case@PowerEdison.COM")]
    [InlineData("UPPER@EV-EDISON.COM")]
    public async Task A_confirmed_account_at_either_company_domain_can_read_data_and_csv(string email)
    {
        var id = f.Users.Add(email);
        var c = f.ClientWith(f.Token(id));
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/sites?pageSize=1")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/sites/export")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/config/maps")).StatusCode);
    }

    [Theory]
    [InlineData("someone@gmail.com")]
    [InlineData("someone@poweredison.co")]
    [InlineData("someone@poweredison.com.evil.io")]
    [InlineData("someone@mail.poweredison.com")]
    [InlineData("someone@sub.ev-edison.com")]
    [InlineData("someone@evedison.com")]
    [InlineData("someone@power-edison.com")]
    [InlineData("someone@poweredison.com.")]
    [InlineData("someone@poweredіson.com")]                     // Cyrillic "і"
    public async Task A_confirmed_account_at_any_other_domain_gets_403_with_a_generic_message(string email)
    {
        var id = f.Users.Add(email);
        var r = await f.ClientWith(f.Token(id)).GetAsync("/api/sites?pageSize=1");
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        var body = await r.Content.ReadAsStringAsync();
        Assert.Contains("not_approved", body);
        Assert.DoesNotContain("domain", body, StringComparison.OrdinalIgnoreCase);       // does not explain the rule
        Assert.DoesNotContain("TEST-0001", body);
        Assert.Equal(HttpStatusCode.Forbidden, await Get(f.Token(id), "/api/sites/export"));
    }

    [Fact]
    public async Task An_unconfirmed_account_gets_401_even_at_a_company_domain()
    {
        var id = f.Users.Add("not.yet.confirmed@poweredison.com", confirmed: false);
        Assert.Equal(HttpStatusCode.Unauthorized, await Get(f.Token(id)));
        Assert.Equal(HttpStatusCode.Unauthorized, await Get(f.Token(id), "/api/sites/export"));

        f.Users.Update(id, a => a with { Confirmed = true });                          // clicks the link
        Assert.Equal(HttpStatusCode.OK, await Get(f.Token(id)));
    }

    [Fact]
    public async Task The_token_email_claim_is_not_trusted_on_its_own()
    {
        // The account's e-mail was changed to a personal address after the token was issued: Supabase's answer wins.
        var id = f.Users.Add("changer@poweredison.com");
        var token = f.Token(id);
        Assert.Equal(HttpStatusCode.OK, await Get(token));
        f.Users.Update(id, a => a with { Email = "changer@gmail.com" });
        Assert.Equal(HttpStatusCode.Forbidden, await Get(token));

        // A token claiming a company address for an account Supabase knows as personal.
        var personal = f.Users.Add("outsider@gmail.com");
        Assert.Equal(HttpStatusCode.Forbidden, await Get(f.Token(personal, email: "outsider@poweredison.com")));
    }

    [Fact]
    public async Task Forged_tokens_for_a_company_address_get_401_and_never_reach_Supabase()
    {
        var id = f.Users.Add("target@poweredison.com");
        var lookups = f.Users.Lookups;
        var attacker = CloudAuthWebAppFactory.NewKey("supabase-test-key");          // same key id, attacker's key
        var forged = CloudAuthWebAppFactory.MakeToken(id, "target@poweredison.com", key: attacker);
        Assert.Equal(HttpStatusCode.Unauthorized, await Get(forged));

        var hs = CloudAuthWebAppFactory.MakeToken(id, "target@poweredison.com",
            key: new SymmetricSecurityKey("a-guessed-secret-that-is-long-enough-32b"u8.ToArray()));
        Assert.Equal(HttpStatusCode.Unauthorized, await Get(hs));

        // Re-signing someone else's payload with the alg header stripped.
        var real = f.Token(id);
        var parts = real.Split('.');
        Assert.Equal(HttpStatusCode.Unauthorized, await Get($"{Base64UrlEncoder.Encode("""{"alg":"none","typ":"JWT"}""")}.{parts[1]}."));
        Assert.Equal(lookups, f.Users.Lookups);
    }

    [Fact]
    public async Task Revocation_deleting_banning_or_signing_out_ends_access_on_the_next_request()
    {
        var deleted = f.Users.Add("leaver1@poweredison.com");
        var t1 = f.Token(deleted);
        Assert.Equal(HttpStatusCode.OK, await Get(t1));
        f.Users.Update(deleted, a => a with { Deleted = true });
        Assert.Equal(HttpStatusCode.Unauthorized, await Get(t1));                        // token still unexpired

        var banned = f.Users.Add("leaver2@ev-edison.com");
        var t2 = f.Token(banned);
        Assert.Equal(HttpStatusCode.OK, await Get(t2));
        f.Users.Update(banned, a => a with { Banned = true });
        Assert.Equal(HttpStatusCode.Unauthorized, await Get(t2, "/api/sites/export"));

        var signedOut = f.Users.Add("phone.lost@poweredison.com");
        var t3 = f.Token(signedOut);
        f.Users.RevokeSession(t3);
        Assert.Equal(HttpStatusCode.Unauthorized, await Get(t3));
    }

    [Fact]
    public async Task A_blocked_user_id_is_refused_immediately_by_configuration()
    {
        var id = f.Users.Add("blocked@poweredison.com");
        var token = f.Token(id);
        Assert.Equal(HttpStatusCode.OK, await Get(token));
        using var host = f.WithWebHostBuilder(b => b.UseSetting("Auth:BlockedUserIds", id.ToString()));
        Assert.Equal(HttpStatusCode.Forbidden, (await f.ClientFor(host, token).GetAsync("/api/sites?pageSize=1")).StatusCode);
    }

    [Fact]
    public async Task Supabase_unreachable_means_503_and_no_data_never_access()
    {
        var token = f.Token(f.Users.Add("during.outage@poweredison.com"));
        f.Users.Down = true;
        try
        {
            var r = await f.ClientWith(token).GetAsync("/api/sites?pageSize=1");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, r.StatusCode);
            Assert.Equal("auth_unavailable", await Code(r));
            Assert.DoesNotContain("TEST-0001", await r.Content.ReadAsStringAsync());
        }
        finally { f.Users.Down = false; }
        Assert.Equal(HttpStatusCode.OK, await Get(token));
    }

    [Fact]
    public async Task Anonymous_and_service_tokens_are_refused_even_with_a_company_email()
    {
        var id = f.Users.Add("svc@poweredison.com");
        Assert.Equal(HttpStatusCode.Forbidden, await Get(f.Token(id, anonymous: true)));
        Assert.Equal(HttpStatusCode.Forbidden, await Get(f.Token(id, role: "service_role")));
    }

    [Fact]
    public async Task Self_registered_employees_stay_read_only()
    {
        var c = f.ClientWith(f.Token(f.Users.Add("reader@ev-edison.com")));
        foreach (var (method, url) in new[] { ("POST", "/api/saved-sites"), ("PUT", "/api/screening"), ("POST", "/api/methodology/versions/1/publish"), ("POST", "/api/admin/import") })
        {
            var r = await c.SendAsync(new HttpRequestMessage(new HttpMethod(method), url) { Content = JsonContent.Create(new { siteId = "TEST-0001", confirm = true }) });
            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
            Assert.Equal("read_only", await Code(r));
        }
    }

    [Fact]
    public async Task Transition_rule_requires_both_the_allowlist_and_a_company_address()
    {
        var personalButApproved = f.Users.Add("old.admin.account@gmail.com");
        var companyApproved = f.Users.Add("approved@poweredison.com");
        var companyNotListed = f.Users.Add("not.listed@ev-edison.com");
        using var host = f.WithWebHostBuilder(b =>
        {
            b.UseSetting("Auth:AccessRule", "EmailDomainAndUserIds");
            b.UseSetting("Auth:AllowedUserIds", $"{personalButApproved},{companyApproved}");
        });
        async Task<HttpStatusCode> As(Guid id) => (await f.ClientFor(host, f.Token(id)).GetAsync("/api/sites?pageSize=1")).StatusCode;
        Assert.Equal(HttpStatusCode.Forbidden, await As(personalButApproved));            // personal address loses access
        Assert.Equal(HttpStatusCode.OK, await As(companyApproved));
        Assert.Equal(HttpStatusCode.Forbidden, await As(companyNotListed));               // self-registration not open yet
    }

    [Fact]
    public async Task Under_the_original_rule_self_registered_accounts_get_nothing()
    {
        // Deploying this code with Auth:AccessRule unset keeps today's behaviour: only listed user ids.
        var selfRegistered = f.Users.Add("self.registered@poweredison.com");
        using var host = f.WithWebHostBuilder(b =>
        {
            b.UseSetting("Auth:AccessRule", "");
            b.UseSetting("Auth:AllowedUserIds", CloudAuthWebAppFactory.ApprovedUser.ToString());
        });
        Assert.Equal(HttpStatusCode.Forbidden, (await f.ClientFor(host, f.Token(selfRegistered)).GetAsync("/api/sites?pageSize=1")).StatusCode);
    }

    public static TheoryData<string, string, string> UnsafeConfigurations => new()
    {
        { "EmailDomain with a leftover allowlist", "EmailDomain", CloudAuthWebAppFactory.ApprovedUser.ToString() },
        { "original rule, empty allowlist", "", "" },
        { "transition rule, empty allowlist", "EmailDomainAndUserIds", "" },
        { "lower-case typo", "emaildomain", "" },
        { "numeric value", "2", "" },
        { "unknown rule", "Everyone", "" },
    };

    [Theory]
    [MemberData(nameof(UnsafeConfigurations))]
    public void The_API_refuses_to_start_with_an_unsafe_access_configuration(string why, string rule, string allowed)
    {
        using var host = f.WithWebHostBuilder(b =>
        {
            b.UseSetting("Auth:AccessRule", rule);
            b.UseSetting("Auth:AllowedUserIds", allowed);
        });
        var ex = Record.Exception(() => host.CreateClient());
        Assert.True(ex is InvalidOperationException, $"{why}: expected a startup error, got {ex?.GetType().Name ?? "none"}");
    }

    [Theory]
    [InlineData("")]
    [InlineData("sb_secret_abc123")]
    public void The_API_refuses_to_start_without_a_publishable_key_or_with_a_secret_one(string key)
    {
        using var host = f.WithWebHostBuilder(b => b.UseSetting("Auth:Supabase:PublishableKey", key));
        Assert.IsType<InvalidOperationException>(Record.Exception(() => host.CreateClient()));
    }
}
