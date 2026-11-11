using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace NJTruckCharging.Tests.Integration;

/// <summary>
/// Self-registration (<c>Auth:AccessRule=EmailDomain</c>) plus one approved administrator exception: a Gmail account
/// named in <c>Auth:DomainExceptionUserIds</c>. Only the domain test is waived for it.
/// </summary>
public sealed class AdminExceptionAuthWebAppFactory : CloudAuthWebAppFactory
{
    public static readonly Guid Admin = Guid.Parse("a11ce000-0000-4000-8000-00000000ad01");
    public const string AdminEmail = "c3.admin.personal@gmail.com";

    protected override string DatabaseName => "NJTruckCharging_AdminExceptionTest";

    public AdminExceptionAuthWebAppFactory() => Users.Add(Admin, AdminEmail);

    protected override void ConfigureAccess(IWebHostBuilder builder)
    {
        builder.UseSetting("Auth:AccessRule", "EmailDomain");
        builder.UseSetting("Auth:DomainExceptionUserIds", Admin.ToString());
    }
}

public class AdministratorExceptionApiTests(AdminExceptionAuthWebAppFactory f) : IClassFixture<AdminExceptionAuthWebAppFactory>, IAsyncLifetime
{
    private static readonly Guid Admin = AdminExceptionAuthWebAppFactory.Admin;

    public Task InitializeAsync() => f.EnsureSeededAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<HttpStatusCode> Get(string token, string url = "/api/sites?pageSize=1") =>
        (await f.ClientWith(token).GetAsync(url)).StatusCode;

    /// <summary>Each test that changes the administrator account restores it, so tests stay independent.</summary>
    private void ResetAdmin() => f.Users.Update(Admin, _ => new FakeSupabaseUsers.Account(AdminExceptionAuthWebAppFactory.AdminEmail));

    [Fact]
    public async Task The_approved_administrator_with_a_personal_address_can_read_data_and_csv()
    {
        ResetAdmin();
        var c = f.ClientWith(f.Token(Admin));
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/sites?pageSize=1")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/sites/export")).StatusCode);
    }

    [Theory]
    [InlineData("someone.else@gmail.com")]
    [InlineData("c3.admin.personal@outlook.com")]
    [InlineData("someone@mail.poweredison.com")]
    public async Task Every_other_personal_or_lookalike_account_is_still_refused(string email)
    {
        var id = f.Users.Add(email);
        Assert.Equal(HttpStatusCode.Forbidden, await Get(f.Token(id)));
    }

    [Fact]
    public async Task Company_addresses_still_self_register_alongside_the_exception()
    {
        Assert.Equal(HttpStatusCode.OK, await Get(f.Token(f.Users.Add("new.hire@poweredison.com"))));
        Assert.Equal(HttpStatusCode.OK, await Get(f.Token(f.Users.Add("new.hire@ev-edison.com"))));
    }

    [Fact]
    public async Task Another_account_cannot_borrow_the_administrators_exception()
    {
        ResetAdmin();
        // A different (real, confirmed) Gmail account presents a token that claims the administrator's e-mail.
        var other = f.Users.Add("impostor@gmail.com");
        Assert.Equal(HttpStatusCode.Forbidden, await Get(f.Token(other, email: AdminExceptionAuthWebAppFactory.AdminEmail)));

        // A token for the administrator's id signed with someone else's key: refused before Supabase is asked.
        var lookups = f.Users.Lookups;
        var forged = CloudAuthWebAppFactory.MakeToken(Admin, AdminExceptionAuthWebAppFactory.AdminEmail,
            key: CloudAuthWebAppFactory.NewKey("supabase-test-key"));
        Assert.Equal(HttpStatusCode.Unauthorized, await Get(forged));
        var hs = CloudAuthWebAppFactory.MakeToken(Admin, AdminExceptionAuthWebAppFactory.AdminEmail,
            key: new SymmetricSecurityKey("a-guessed-secret-that-is-long-enough-32b"u8.ToArray()));
        Assert.Equal(HttpStatusCode.Unauthorized, await Get(hs));
        Assert.Equal(lookups, f.Users.Lookups);
    }

    [Fact]
    public async Task The_administrator_still_needs_a_confirmed_email()
    {
        ResetAdmin();
        var token = f.Token(Admin);
        f.Users.Update(Admin, a => a with { Confirmed = false });
        try { Assert.Equal(HttpStatusCode.Unauthorized, await Get(token)); }
        finally { ResetAdmin(); }
    }

    [Fact]
    public async Task A_revoked_administrator_banned_deleted_or_signed_out_loses_access()
    {
        ResetAdmin();
        var token = f.Token(Admin);
        Assert.Equal(HttpStatusCode.OK, await Get(token));
        try
        {
            f.Users.Update(Admin, a => a with { Banned = true });
            Assert.Equal(HttpStatusCode.Unauthorized, await Get(token));
            Assert.Equal(HttpStatusCode.Unauthorized, await Get(token, "/api/sites/export"));

            ResetAdmin();
            Assert.Equal(HttpStatusCode.OK, await Get(token));
            f.Users.Update(Admin, a => a with { Deleted = true });
            Assert.Equal(HttpStatusCode.Unauthorized, await Get(token));

            ResetAdmin();
            var session = f.Token(Admin);
            f.Users.RevokeSession(session);
            Assert.Equal(HttpStatusCode.Unauthorized, await Get(session));
        }
        finally { ResetAdmin(); }
    }

    [Fact]
    public async Task Removing_the_exception_or_blocking_the_administrator_ends_access()
    {
        ResetAdmin();
        var token = f.Token(Admin);
        Assert.Equal(HttpStatusCode.OK, await Get(token));

        using var removed = f.WithWebHostBuilder(b => b.UseSetting("Auth:DomainExceptionUserIds", ""));
        var c1 = removed.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        c1.DefaultRequestHeaders.Authorization = new("Bearer", token);
        Assert.Equal(HttpStatusCode.Forbidden, (await c1.GetAsync("/api/sites?pageSize=1")).StatusCode);

        using var blocked = f.WithWebHostBuilder(b =>
        {
            b.UseSetting("Auth:DomainExceptionUserIds", "");
            b.UseSetting("Auth:BlockedUserIds", Admin.ToString());
        });
        var c2 = blocked.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        c2.DefaultRequestHeaders.Authorization = new("Bearer", token);
        Assert.Equal(HttpStatusCode.Forbidden, (await c2.GetAsync("/api/sites?pageSize=1")).StatusCode);
    }

    [Fact]
    public async Task The_administrator_exception_never_lifts_read_only_mode()
    {
        ResetAdmin();
        var c = f.ClientWith(f.Token(Admin));
        foreach (var (method, url) in new[] { ("POST", "/api/saved-sites"), ("POST", "/api/methodology/versions/1/publish"), ("POST", "/api/admin/import") })
        {
            var r = await c.SendAsync(new HttpRequestMessage(new HttpMethod(method), url) { Content = JsonContent.Create(new { confirm = true }) });
            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
            Assert.Equal("read_only", (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }
    }

    [Fact]
    public async Task Supabase_unreachable_gives_the_administrator_503_not_access()
    {
        ResetAdmin();
        var token = f.Token(Admin);
        f.Users.Down = true;
        try { Assert.Equal(HttpStatusCode.ServiceUnavailable, await Get(token)); }
        finally { f.Users.Down = false; }
    }

    [Fact]
    public async Task Transition_rule_keeps_the_administrator_only_when_also_on_the_allowlist()
    {
        ResetAdmin();
        var token = f.Token(Admin);
        using var host = f.WithWebHostBuilder(b =>
        {
            b.UseSetting("Auth:AccessRule", "EmailDomainAndUserIds");
            b.UseSetting("Auth:AllowedUserIds", Admin.ToString());
        });
        var c = host.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        c.DefaultRequestHeaders.Authorization = new("Bearer", token);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/sites?pageSize=1")).StatusCode);

        using var noException = f.WithWebHostBuilder(b =>
        {
            b.UseSetting("Auth:AccessRule", "EmailDomainAndUserIds");
            b.UseSetting("Auth:AllowedUserIds", Admin.ToString());
            b.UseSetting("Auth:DomainExceptionUserIds", "");
        });
        var c2 = noException.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        c2.DefaultRequestHeaders.Authorization = new("Bearer", token);
        Assert.Equal(HttpStatusCode.Forbidden, (await c2.GetAsync("/api/sites?pageSize=1")).StatusCode);
    }

    [Fact]
    public void Exceptions_with_the_original_UserIds_rule_stop_startup()
    {
        using var host = f.WithWebHostBuilder(b =>
        {
            b.UseSetting("Auth:AccessRule", "");
            b.UseSetting("Auth:AllowedUserIds", Admin.ToString());
        });
        Assert.IsType<InvalidOperationException>(Record.Exception(() => host.CreateClient()));
    }
}
