using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NJTruckCharging.Infrastructure.Persistence;

namespace NJTruckCharging.Tests.Integration;

/// <summary>
/// The production topology: the dashboard (https://c3dashboard.netlify.app) and the API (onrender.com) are different
/// sites, so every legitimate browser request carries "Origin: https://c3dashboard.netlify.app" and
/// "Sec-Fetch-Site: cross-site". These requests must work; any other origin must not.
/// </summary>
public partial class MethodologyCloudEditingApiTests
{
    private const string Dashboard = MethodologyEditingWebAppFactory.DashboardOrigin;

    private async Task<HttpResponseMessage> FromBrowser(HttpMethod method, string url, object? body, string? origin, string? fetchSite = "cross-site",
        Guid? user = null, bool withToken = true)
    {
        var req = new HttpRequestMessage(method, url);
        if (body is not null) req.Content = JsonContent.Create(body, options: J);
        if (origin is not null) req.Headers.Add("Origin", origin);
        if (fetchSite is not null) { req.Headers.Add("Sec-Fetch-Site", fetchSite); req.Headers.Add("Sec-Fetch-Mode", "cors"); }
        var c = withToken ? As(user ?? Employee) : f.ClientWith(null);
        return await c.SendAsync(req);
    }

    private async Task<string> LiveScoresAsync()
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.CandidateSites.AsNoTracking().OrderBy(s => s.Id)
            .Select(s => new { s.Id, s.Score, s.ComputedRank, s.ScoreVersionId, s.TruckDemandPoints }).ToListAsync();
        return JsonSerializer.Serialize(rows);
    }

    [Fact]
    public async Task The_dashboard_can_edit_cross_site_from_its_exact_origin_and_every_step_is_audited()
    {
        // create
        var created = await FromBrowser(HttpMethod.Post, "/api/methodology/drafts", new { name = "From the Netlify dashboard" }, Dashboard);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var d = await Json(created);
        var id = d.GetProperty("version").GetProperty("id").GetInt32();
        var rules = JsonSerializer.Deserialize<NJTruckCharging.Domain.Methodology.MethodologyRules>(d.GetProperty("rules").GetRawText(), J)!;
        // validate + save (expected revision) + conflicting save + review + delete, all as the browser sends them
        Assert.Equal(HttpStatusCode.OK, (await FromBrowser(HttpMethod.Post, "/api/methodology/validate", rules, Dashboard)).StatusCode);
        var edited = Edit(rules, "openLand", x => x with { Cases = [.. x.Cases.Select(k => k with { Points = k.Points + 1 })] });
        var saved = await FromBrowser(HttpMethod.Put, $"/api/methodology/drafts/{id}", new { name = "From the Netlify dashboard", rules = edited, expectedRevision = 1 }, Dashboard);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal(2, (await Json(saved)).GetProperty("version").GetProperty("revision").GetInt32());
        var stale = await FromBrowser(HttpMethod.Put, $"/api/methodology/drafts/{id}", new { name = "stale tab", rules, expectedRevision = 1 }, Dashboard, user: Colleague);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Contains("draft_conflict", await stale.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await FromBrowser(HttpMethod.Post, $"/api/methodology/versions/{id}/preview?topN=5", null, Dashboard)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await FromBrowser(HttpMethod.Delete, $"/api/methodology/drafts/{id}?revision=2", null, Dashboard)).StatusCode);

        var log = await ChangesAsync(id);
        Assert.Equal(["DeleteDraft", "Preview", "SaveDraft", "CreateDraft"], log.Select(e => e.GetProperty("action").GetString()!).ToArray());
        Assert.All(log, e =>
        {
            Assert.Equal(Employee.ToString(), e.GetProperty("actorUserId").GetString());
            Assert.Equal(MethodologyEditingWebAppFactory.EmployeeEmail, e.GetProperty("actorEmail").GetString());
        });
    }

    [Fact]
    public async Task Saving_drafts_from_the_dashboard_never_changes_live_scores_or_the_active_version()
    {
        var active = await ActiveIdAsync();
        var before = await LiveScoresAsync();
        var d = await Json(await FromBrowser(HttpMethod.Post, "/api/methodology/drafts", new { name = "Big change, not applied" }, Dashboard));
        var id = d.GetProperty("version").GetProperty("id").GetInt32();
        var rules = JsonSerializer.Deserialize<NJTruckCharging.Domain.Methodology.MethodologyRules>(d.GetProperty("rules").GetRawText(), J)!;
        var big = Edit(Edit(rules, "exit", x => x with { MaxPoints = 40 }), "port", x => x with { DefaultPoints = 9 });
        Assert.Equal(HttpStatusCode.OK, (await FromBrowser(HttpMethod.Put, $"/api/methodology/drafts/{id}", new { name = "Big change, not applied", rules = big, expectedRevision = 1 }, Dashboard)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await FromBrowser(HttpMethod.Post, $"/api/methodology/versions/{id}/preview?topN=5", null, Dashboard)).StatusCode);
        Assert.Equal(before, await LiveScoresAsync());       // score, rank, version and breakdown inputs of every site unchanged
        Assert.Equal(active, await ActiveIdAsync());
    }

    [Theory]
    [InlineData("https://evil.example")]
    [InlineData("https://c3dashboard.netlify.app.evil.com")]     // lookalike suffix
    [InlineData("https://evilc3dashboard.netlify.app")]          // lookalike prefix
    [InlineData("http://c3dashboard.netlify.app")]               // not https
    [InlineData("https://c3dashboard.netlify.app:8443")]         // other port
    [InlineData("https://c3dashboard.netlify.app/")]             // browsers never send a trailing slash
    [InlineData("https://deploy-preview-7--c3dashboard.netlify.app")]
    [InlineData("null")]                                         // sandboxed iframe / file:// pages
    public async Task Untrusted_origins_are_refused_even_with_a_valid_token(string origin)
    {
        var before = (await Json(await Employee_.GetAsync("/api/methodology/versions"))).GetArrayLength();
        var r = await FromBrowser(HttpMethod.Post, "/api/methodology/drafts", new { name = "should not exist" }, origin);
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Contains("Cross-site methodology changes are not allowed", await r.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Forbidden, (await FromBrowser(HttpMethod.Put, "/api/methodology/drafts/1", new { name = "x", expectedRevision = 1 }, origin)).StatusCode);
        Assert.Equal(before, (await Json(await Employee_.GetAsync("/api/methodology/versions"))).GetArrayLength());
    }

    [Theory]
    [InlineData("cross-site")]
    [InlineData("same-site")]
    public async Task A_browser_request_without_an_origin_is_refused(string fetchSite) =>
        Assert.Equal(HttpStatusCode.Forbidden, (await FromBrowser(HttpMethod.Post, "/api/methodology/drafts", new { name = "x" }, null, fetchSite)).StatusCode);

    [Fact]
    public async Task The_trusted_origin_does_not_replace_sign_in()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await FromBrowser(HttpMethod.Post, "/api/methodology/drafts", new { name = "x" }, Dashboard, withToken: false)).StatusCode);
        var forged = CloudAuthWebAppFactory.MakeToken(Employee, MethodologyEditingWebAppFactory.EmployeeEmail, key: CloudAuthWebAppFactory.NewKey("supabase-test-key"));
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/methodology/drafts") { Content = JsonContent.Create(new { name = "x" }) };
        req.Headers.Add("Origin", Dashboard); req.Headers.Add("Sec-Fetch-Site", "cross-site");
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.ClientWith(forged).SendAsync(req)).StatusCode);
        // a signed-in account that is not admitted stays refused
        var outsider = f.Users.Add("someone.else@gmail.com");
        Assert.Equal(HttpStatusCode.Forbidden, (await FromBrowser(HttpMethod.Post, "/api/methodology/drafts", new { name = "x" }, Dashboard, user: outsider)).StatusCode);
    }

    [Fact]
    public async Task Other_writes_from_the_trusted_dashboard_origin_stay_read_only()
    {
        foreach (var (m, url, body) in new (HttpMethod, string, object)[]
        {
            (HttpMethod.Post, "/api/saved-sites", new { siteId = "TEST-0001" }),
            (HttpMethod.Post, "/api/contacts", new { siteId = "TEST-0001", name = "x" }),
            (HttpMethod.Post, "/api/custom-fields", new { label = "x", type = "Boolean", source = "Manual" }),
            (HttpMethod.Post, "/api/admin/rescore", new { }),
        })
        {
            var r = await FromBrowser(m, url, body, Dashboard);
            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
            Assert.Contains("read_only", await r.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task Cors_preflight_allows_only_the_exact_dashboard_origin()
    {
        async Task<HttpResponseMessage> Preflight(string origin)
        {
            var req = new HttpRequestMessage(HttpMethod.Options, "/api/methodology/drafts/1");
            req.Headers.Add("Origin", origin);
            req.Headers.Add("Access-Control-Request-Method", "PUT");
            req.Headers.Add("Access-Control-Request-Headers", "authorization,content-type");
            return await f.ClientWith(null).SendAsync(req);
        }
        var ok = await Preflight(Dashboard);
        Assert.Equal(Dashboard, ok.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Contains("PUT", string.Join(",", ok.Headers.GetValues("Access-Control-Allow-Methods")));
        var evil = await Preflight("https://evil.example");
        Assert.False(evil.Headers.Contains("Access-Control-Allow-Origin"));
    }
}
