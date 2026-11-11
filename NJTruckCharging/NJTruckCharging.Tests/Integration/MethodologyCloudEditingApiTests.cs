using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using NJTruckCharging.Application.Methodology;
using NJTruckCharging.Domain.Methodology;

namespace NJTruckCharging.Tests.Integration;

/// <summary>
/// The cloud configuration with shared methodology editing switched on: Supabase sign-in, self-registration by company
/// domain plus one Gmail administrator exception, read-only mode ON, and <c>Methodology:CloudEditing:Enabled=true</c>.
/// (On SQL Server the writer connection is the test database itself; the PostgreSQL role is tested end to end by
/// e2e/methodology-editing.)
/// </summary>
public sealed class MethodologyEditingWebAppFactory : CloudAuthWebAppFactory
{
    public static readonly Guid Employee = Guid.Parse("e1e1e1e1-0000-4000-8000-0000000000e1");
    public const string EmployeeEmail = "rules.editor@poweredison.com";
    public static readonly Guid Colleague = Guid.Parse("e2e2e2e2-0000-4000-8000-0000000000e2");
    public const string ColleagueEmail = "second.editor@ev-edison.com";
    public static readonly Guid Admin = Guid.Parse("a11ce000-0000-4000-8000-00000000ad01");
    public const string AdminEmail = "c3.admin.personal@gmail.com";
    /// <summary>The production dashboard origin (Render: Cors__Origins__0).</summary>
    public const string DashboardOrigin = "https://c3dashboard.netlify.app";

    protected override string DatabaseName => "NJTruckCharging_MethodologyEditingTest";

    public MethodologyEditingWebAppFactory()
    {
        Users.Add(Employee, EmployeeEmail);
        Users.Add(Colleague, ColleagueEmail);
        Users.Add(Admin, AdminEmail);
    }

    protected override void ConfigureAccess(IWebHostBuilder builder)
    {
        builder.UseSetting("Auth:AccessRule", "EmailDomain");
        builder.UseSetting("Auth:DomainExceptionUserIds", Admin.ToString());
        builder.UseSetting("Methodology:CloudEditing:Enabled", "true");
        builder.UseSetting("Cors:Origins:0", DashboardOrigin);
    }
}

public partial class MethodologyCloudEditingApiTests(MethodologyEditingWebAppFactory f) : IClassFixture<MethodologyEditingWebAppFactory>, IAsyncLifetime
{
    private static readonly JsonSerializerOptions J = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };
    private static readonly Guid Employee = MethodologyEditingWebAppFactory.Employee;
    private static readonly Guid Colleague = MethodologyEditingWebAppFactory.Colleague;
    private static readonly Guid Admin = MethodologyEditingWebAppFactory.Admin;

    public Task InitializeAsync() => f.EnsureSeededAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient As(Guid user) => f.ClientWith(f.Token(user));
    private HttpClient Employee_ => As(Employee);

    private void Reset(Guid id, string email) => f.Users.Update(id, _ => new FakeSupabaseUsers.Account(email));

    // ------------------------------------------------------------------------------------------------------------
    // Who may edit
    // ------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Signed_out_unapproved_and_lookalike_accounts_cannot_change_the_methodology()
    {
        var anon = f.ClientWith(null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsJsonAsync("/api/methodology/drafts", new { name = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsJsonAsync("/api/methodology/validate", BaselineMethodology.Create(), J)).StatusCode);

        foreach (var email in new[] { "someone@gmail.com", "x@mail.poweredison.com", "x@poweredison.com.evil.io" })
        {
            var c = As(f.Users.Add(email));
            Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsJsonAsync("/api/methodology/drafts", new { name = "x" })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsync("/api/methodology/versions/1/preview", null)).StatusCode);
        }
        // An unconfirmed company address cannot sign in at all.
        var unconfirmed = As(f.Users.Add("not.confirmed@poweredison.com", confirmed: false));
        Assert.Equal(HttpStatusCode.Unauthorized, (await unconfirmed.PostAsJsonAsync("/api/methodology/drafts", new { name = "x" })).StatusCode);
    }

    [Fact]
    public async Task Banned_deleted_or_signed_out_editors_are_refused_immediately()
    {
        var id = f.Users.Add("soon.revoked@poweredison.com");
        var token = f.Token(id);
        var draft = await Json(await f.ClientWith(token).PostAsJsonAsync("/api/methodology/drafts", new { name = "revocation" }));
        var draftId = draft.GetProperty("version").GetProperty("id").GetInt32();

        f.Users.Update(id, a => a with { Banned = true });
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.ClientWith(token).PostAsync($"/api/methodology/versions/{draftId}/preview", null)).StatusCode);
        f.Users.Update(id, a => a with { Banned = false, Deleted = true });
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.ClientWith(token).DeleteAsync($"/api/methodology/drafts/{draftId}?revision=1")).StatusCode);
        f.Users.Update(id, a => a with { Deleted = false });
        var session = f.Token(id);
        f.Users.RevokeSession(session);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.ClientWith(session).PostAsJsonAsync("/api/methodology/drafts", new { name = "x" })).StatusCode);

        // Nothing the revoked account attempted was recorded as a change.
        var log = await ChangesAsync(draftId);
        Assert.Single(log);   // only the original CreateDraft
    }

    [Fact]
    public async Task Removing_the_administrator_exception_or_blocking_an_editor_ends_editing_rights()
    {
        Reset(Admin, MethodologyEditingWebAppFactory.AdminEmail);
        var token = f.Token(Admin);
        using var removed = f.WithWebHostBuilder(b => b.UseSetting("Auth:DomainExceptionUserIds", ""));
        var c1 = removed.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        c1.DefaultRequestHeaders.Authorization = new("Bearer", token);
        Assert.Equal(HttpStatusCode.Forbidden, (await c1.PostAsJsonAsync("/api/methodology/drafts", new { name = "x" })).StatusCode);

        using var blocked = f.WithWebHostBuilder(b => b.UseSetting("Auth:BlockedUserIds", Employee.ToString()));
        var c2 = blocked.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        c2.DefaultRequestHeaders.Authorization = new("Bearer", f.Token(Employee));
        Assert.Equal(HttpStatusCode.Forbidden, (await c2.PostAsJsonAsync("/api/methodology/drafts", new { name = "x" })).StatusCode);
    }

    [Fact]
    public async Task Cross_site_browser_requests_are_refused_even_with_a_valid_token()
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/methodology/drafts") { Content = JsonContent.Create(new { name = "x" }) };
        req.Headers.Add("Origin", "https://evil.example");
        Assert.Equal(HttpStatusCode.Forbidden, (await Employee_.SendAsync(req)).StatusCode);
        var req2 = new HttpRequestMessage(HttpMethod.Post, "/api/methodology/drafts") { Content = JsonContent.Create(new { name = "x" }) };
        req2.Headers.Add("Sec-Fetch-Site", "cross-site");
        Assert.Equal(HttpStatusCode.Forbidden, (await Employee_.SendAsync(req2)).StatusCode);
    }

    // ------------------------------------------------------------------------------------------------------------
    // Read-only mode stays in force for everything else
    // ------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Every_other_change_is_still_refused_by_read_only_mode()
    {
        var c = Employee_;
        Assert.True((await Json(await c.GetAsync("/api/methodology/editing"))).GetProperty("enabled").GetBoolean());
        foreach (var r in new[]
        {
            await c.PostAsJsonAsync("/api/saved-sites", new { siteId = "TEST-0001" }),
            await c.PostAsJsonAsync("/api/contacts", new { siteId = "TEST-0001", name = "x" }),
            await c.PutAsJsonAsync("/api/feasibility", new { siteId = "TEST-0001", itemKey = "Zoning", status = "Concern" }),
            await c.PostAsJsonAsync("/api/custom-fields", new { label = "x", type = "Boolean", source = "Manual" }),
            await c.PostAsync("/api/admin/rescore", null),
        })
        {
            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
            Assert.Contains("read_only", await r.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task With_editing_switched_off_the_methodology_is_read_only_again()
    {
        using var off = f.WithWebHostBuilder(b => b.UseSetting("Methodology:CloudEditing:Enabled", "false"));
        var c = off.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        c.DefaultRequestHeaders.Authorization = new("Bearer", f.Token(Employee));
        var status = await Json(await c.GetAsync("/api/methodology/editing"));
        Assert.False(status.GetProperty("enabled").GetBoolean());
        foreach (var r in new[]
        {
            await c.PostAsJsonAsync("/api/methodology/drafts", new { name = "x" }),
            await c.PostAsJsonAsync("/api/methodology/validate", BaselineMethodology.Create(), J),
            await c.PostAsync("/api/methodology/versions/1/preview", null),
        })
        {
            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
            Assert.Contains("read_only", await r.Content.ReadAsStringAsync());
        }
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/methodology/active")).StatusCode);
    }

    // ------------------------------------------------------------------------------------------------------------
    // The full workflow, consistency and audit
    // ------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task An_employee_edits_and_publishes_scores_ranks_exports_stay_consistent_and_everything_is_audited()
    {
        var c = Employee_;
        var activeBefore = await ActiveIdAsync();
        var (id, rules, rev) = await NewDraftAsync(c, "Open land 3+ acres worth more");
        Assert.Equal(1, rev);
        var draft = await Json(await c.GetAsync($"/api/methodology/versions/{id}"));
        Assert.Equal(MethodologyEditingWebAppFactory.EmployeeEmail, draft.GetProperty("version").GetProperty("createdByEmail").GetString());

        // Change thresholds, points and an explanation of the four headline criteria.
        rules = Edit(rules, "exit", x => x with { MaxPoints = 25, FullAt = 0.5, ZeroAt = 4, Explanation = "Closer exits score more (edited)." });
        rules = Edit(rules, "openLand", x => x with { Cases = [.. x.Cases.Select((k, i) => i == 0 ? k with { Points = k.Points + 5 } : k)] });
        rules = Edit(rules, "propertyClass", x => x with { DefaultPoints = 1 });
        rules = Edit(rules, "truckingOnSite", x => x with { Cases = [.. x.Cases.Select(k => k with { Points = k.Points + 2 })] });
        var saved = await SaveAsync(c, id, rules, rev);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var savedJson = await Json(saved);
        Assert.Equal(2, savedJson.GetProperty("version").GetProperty("revision").GetInt32());
        Assert.Equal(MethodologyEditingWebAppFactory.EmployeeEmail, savedJson.GetProperty("version").GetProperty("updatedByEmail").GetString());

        var preview = await Json(await c.PostAsync($"/api/methodology/versions/{id}/preview?topN=5", null));
        Assert.True(preview.GetProperty("scoresChanged").GetInt32() > 0);
        var publish = await c.PostAsJsonAsync($"/api/methodology/versions/{id}/publish",
            new { confirm = true, expectedActiveVersionId = activeBefore, rulesHash = preview.GetProperty("rulesHash").GetString(), note = "team decision" });
        Assert.Equal(HttpStatusCode.OK, publish.StatusCode);
        Assert.Equal(id.ToString(), publish.Headers.GetValues("X-Methodology-Version").Single());
        Assert.Equal(id, await ActiveIdAsync());

        // Every view reads the same, freshly materialised scores and ranks.
        var list = await Json(await c.GetAsync("/api/sites?pageSize=8&sortBy=Score&descending=true"));
        foreach (var s in list.GetProperty("items").EnumerateArray())
        {
            var siteId = s.GetProperty("siteId").GetString()!;
            var p = (await Json(await c.GetAsync($"/api/methodology/versions/{id}/comparison?search={siteId}&pageSize=5")))
                .GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("siteId").GetString() == siteId);
            // Live score and rank == the published version's stored score and rank.
            Assert.Equal(p.GetProperty("proposedScore").GetDouble(), s.GetProperty("score").GetDouble());
            Assert.Equal(p.GetProperty("proposedRank").GetInt32(), s.GetProperty("rank").GetInt32());
            Assert.Equal(p.GetProperty("activeScore").GetDouble(), p.GetProperty("proposedScore").GetDouble());
        }
        var csv = await (await c.GetAsync("/api/sites/export")).Content.ReadAsStringAsync();
        var top = list.GetProperty("items")[0];
        Assert.Contains(top.GetProperty("siteId").GetString()!, csv);
        var detail = await Json(await c.GetAsync($"/api/sites/{top.GetProperty("id").GetInt32()}"));
        Assert.Equal(top.GetProperty("score").GetDouble(), detail.GetProperty("summary").GetProperty("score").GetDouble());

        // Audit: who, when, previous → new values, for every step.
        var log = await ChangesAsync(id);
        Assert.Equal(["Publish", "Preview", "SaveDraft", "CreateDraft"], log.Select(e => e.GetProperty("action").GetString()!).ToArray());
        Assert.All(log, e =>
        {
            Assert.Equal(Employee.ToString(), e.GetProperty("actorUserId").GetString());
            Assert.Equal(MethodologyEditingWebAppFactory.EmployeeEmail, e.GetProperty("actorEmail").GetString());
        });
        var save = log.Single(e => e.GetProperty("action").GetString() == "SaveDraft");
        Assert.Equal(1, save.GetProperty("revisionBefore").GetInt32());
        Assert.Equal(2, save.GetProperty("revisionAfter").GetInt32());
        Assert.Contains(save.GetProperty("changes").EnumerateArray(), x => x.GetProperty("key").GetString() == "exit");
        var snap = await Json(await c.GetAsync($"/api/methodology/changes/{save.GetProperty("id").GetInt64()}/snapshot"));
        var beforeExit = snap.GetProperty("before").GetProperty("rules").GetProperty("criteria").EnumerateArray().Single(x => x.GetProperty("key").GetString() == "exit");
        var afterExit = snap.GetProperty("after").GetProperty("rules").GetProperty("criteria").EnumerateArray().Single(x => x.GetProperty("key").GetString() == "exit");
        Assert.NotEqual(25, beforeExit.GetProperty("maxPoints").GetDouble());
        Assert.Equal(25, afterExit.GetProperty("maxPoints").GetDouble());
        var pub = log[0];
        Assert.Equal(activeBefore, pub.GetProperty("previousActiveVersionId").GetInt32());
        Assert.Equal("team decision", pub.GetProperty("note").GetString());
        var act = (await Json(await c.GetAsync("/api/methodology/activations")))[0];
        Assert.Equal(MethodologyEditingWebAppFactory.EmployeeEmail, act.GetProperty("actorEmail").GetString());
        Assert.Equal(Employee.ToString(), act.GetProperty("actorUserId").GetString());
        var version = (await Json(await c.GetAsync($"/api/methodology/versions/{id}"))).GetProperty("version");
        Assert.Equal(MethodologyEditingWebAppFactory.EmployeeEmail, version.GetProperty("publishedByEmail").GetString());

        // Rollback: the Gmail administrator (exception) restores the previous version; history is complete.
        Reset(Admin, MethodologyEditingWebAppFactory.AdminEmail);
        var admin = As(Admin);
        var rp = await Json(await admin.PostAsync($"/api/methodology/versions/{activeBefore}/preview?topN=5", null));
        var restore = await admin.PostAsJsonAsync($"/api/methodology/versions/{activeBefore}/publish",
            new { confirm = true, expectedActiveVersionId = id, rulesHash = rp.GetProperty("rulesHash").GetString(), note = "rollback" });
        Assert.Equal(HttpStatusCode.OK, restore.StatusCode);
        Assert.Equal(activeBefore, await ActiveIdAsync());
        var restored = (await ChangesAsync(activeBefore))[0];
        Assert.Equal("Restore", restored.GetProperty("action").GetString());
        Assert.Equal(Admin.ToString(), restored.GetProperty("actorUserId").GetString());
        Assert.Equal(MethodologyEditingWebAppFactory.AdminEmail, restored.GetProperty("actorEmail").GetString());
        // The published version is kept (immutable) and can be restored again later.
        Assert.Equal("Published", (await Json(await c.GetAsync($"/api/methodology/versions/{id}"))).GetProperty("version").GetProperty("status").GetString());
    }

    // ------------------------------------------------------------------------------------------------------------
    // Concurrency, validation, overlapping jobs, rate limits, failures
    // ------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_save_based_on_an_outdated_revision_is_refused_and_names_the_other_editor()
    {
        var (id, rules, rev) = await NewDraftAsync(Employee_, "Concurrent");
        var mine = Edit(rules, "port", x => x with { DefaultPoints = 1 });
        var theirs = Edit(rules, "port", x => x with { DefaultPoints = 2 });
        Assert.Equal(HttpStatusCode.OK, (await SaveAsync(As(Colleague), id, theirs, rev)).StatusCode);

        var stale = await SaveAsync(Employee_, id, mine, rev);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var body = await stale.Content.ReadAsStringAsync();
        Assert.Contains("draft_conflict", body);
        Assert.Contains(MethodologyEditingWebAppFactory.ColleagueEmail, body);
        // The colleague's save is intact.
        var now = await Json(await Employee_.GetAsync($"/api/methodology/versions/{id}"));
        Assert.Equal(2, Port(now.GetProperty("rules")).GetProperty("defaultPoints").GetDouble());

        // Deleting with an outdated revision is refused too; with the current one it works.
        Assert.Equal(HttpStatusCode.Conflict, (await Employee_.DeleteAsync($"/api/methodology/drafts/{id}?revision={rev}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Employee_.DeleteAsync($"/api/methodology/drafts/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Employee_.DeleteAsync($"/api/methodology/drafts/{id}?revision={rev + 1}")).StatusCode);
        var deleted = (await ChangesAsync(id))[0];
        Assert.Equal("DeleteDraft", deleted.GetProperty("action").GetString());
        Assert.True(deleted.GetProperty("hasSnapshots").GetBoolean());   // the deleted rules are kept in the audit
    }

    [Fact]
    public async Task Simultaneous_saves_of_the_same_revision_let_exactly_one_win()
    {
        var (id, rules, rev) = await NewDraftAsync(Employee_, "Race");
        var results = await Task.WhenAll(Enumerable.Range(1, 6).Select(i =>
            SaveAsync(i % 2 == 0 ? Employee_ : As(Colleague), id, Edit(rules, "port", x => x with { DefaultPoints = i }), rev)));
        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.All(results.Where(r => r.StatusCode != HttpStatusCode.OK), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        Assert.Equal(2, (await Json(await Employee_.GetAsync($"/api/methodology/versions/{id}"))).GetProperty("version").GetProperty("revision").GetInt32());
        Assert.Equal(1, (await ChangesAsync(id)).Count(e => e.GetProperty("action").GetString() == "SaveDraft"));
    }

    [Fact]
    public async Task Invalid_scoring_values_and_thresholds_are_rejected_before_saving_and_publishing()
    {
        var c = Employee_;
        var (id, rules, rev) = await NewDraftAsync(c, "Invalid");
        foreach (var bad in new[]
        {
            Edit(rules, "exit", x => x with { MaxPoints = 1000 }),                           // points out of range
            Edit(rules, "exit", x => x with { ZeroAt = null }),                              // missing threshold
            Edit(rules, "openLand", x => x with { Cases = [.. x.Cases.Select(k => k with { Points = double.NaN })] }),
            rules with { Criteria = [.. rules.Criteria, rules.Criteria[0]] },                 // duplicate key
        })
        {
            var r = await SaveRawAsync(c, id, bad, rev);
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync($"/api/methodology/drafts/{id}", new { name = "", rules, expectedRevision = rev }, J)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync($"/api/methodology/drafts/{id}", new { name = "no revision", rules }, J)).StatusCode);
        // Nothing was saved: still revision 1, and only the CreateDraft audit entry exists.
        Assert.Equal(1, (await Json(await c.GetAsync($"/api/methodology/versions/{id}"))).GetProperty("version").GetProperty("revision").GetInt32());
        Assert.Single(await ChangesAsync(id));
        // Publishing an un-reviewed draft or without confirmation is refused.
        var active = await ActiveIdAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync($"/api/methodology/versions/{id}/publish",
            new { confirm = false, expectedActiveVersionId = active, rulesHash = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync($"/api/methodology/versions/{id}/publish",
            new { confirm = true, expectedActiveVersionId = active, rulesHash = "x" })).StatusCode);
        Assert.Equal(active, await ActiveIdAsync());
    }

    [Fact]
    public async Task Duplicate_publish_requests_apply_the_version_only_once()
    {
        var c = Employee_;
        var active = await ActiveIdAsync();
        var (id, rules, rev) = await NewDraftAsync(c, "Double click");
        await SaveAsync(c, id, Edit(rules, "port", x => x with { DefaultPoints = 3 }), rev);
        var hash = (await Json(await c.PostAsync($"/api/methodology/versions/{id}/preview?topN=5", null))).GetProperty("rulesHash").GetString();
        var activationsBefore = (await Json(await c.GetAsync("/api/methodology/activations"))).GetArrayLength();

        var body = new { confirm = true, expectedActiveVersionId = active, rulesHash = hash };
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => As(Employee).PostAsJsonAsync($"/api/methodology/versions/{id}/publish", body)));
        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.All(results.Where(r => r.StatusCode != HttpStatusCode.OK), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        Assert.Equal(activationsBefore + 1, (await Json(await c.GetAsync("/api/methodology/activations"))).GetArrayLength());
        Assert.Equal(1, (await ChangesAsync(id)).Count(e => e.GetProperty("action").GetString() == "Publish"));
        await RestoreAsync(active, id);
    }

    [Fact]
    public async Task A_recalculation_is_refused_while_another_is_running()
    {
        var (id, _, _) = await NewDraftAsync(Employee_, "Overlap");
        var gate = f.Services.GetRequiredService<ScoringJobGate>();
        using (var running = gate.TryEnter())
        {
            Assert.NotNull(running);
            var r = await Employee_.PostAsync($"/api/methodology/versions/{id}/preview", null);
            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            Assert.Contains("scoring_busy", await r.Content.ReadAsStringAsync());
        }
        Assert.Equal(HttpStatusCode.OK, (await Employee_.PostAsync($"/api/methodology/versions/{id}/preview?topN=5", null)).StatusCode);
    }

    [Fact]
    public async Task Expensive_recalculations_are_rate_limited_per_user()
    {
        using var limited = f.WithWebHostBuilder(b => b.UseSetting("Methodology:RateLimits:ScoringPerWindow", "2"));
        HttpClient Client(Guid u)
        {
            var c = limited.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
            c.DefaultRequestHeaders.Authorization = new("Bearer", f.Token(u));
            return c;
        }
        var d = await Json(await Client(Employee).PostAsJsonAsync("/api/methodology/drafts", new { name = "rate" }));
        var id = d.GetProperty("version").GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.OK, (await Client(Employee).PostAsync($"/api/methodology/versions/{id}/preview?topN=5", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Client(Employee).PostAsync($"/api/methodology/versions/{id}/preview?topN=5", null)).StatusCode);
        var third = await Client(Employee).PostAsync($"/api/methodology/versions/{id}/preview?topN=5", null);
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.True(third.Headers.Contains("Retry-After"));
        Assert.Contains("rate_limited", await third.Content.ReadAsStringAsync());
        // Per user: a colleague is not affected; ordinary reads are never limited.
        Assert.Equal(HttpStatusCode.OK, (await Client(Colleague).PostAsync($"/api/methodology/versions/{id}/preview?topN=5", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Client(Employee).GetAsync("/api/methodology/active")).StatusCode);
    }

    [Fact]
    public async Task A_database_failure_while_publishing_rolls_everything_back_and_reveals_no_details()
    {
        var c = Employee_;
        var active = await ActiveIdAsync();
        var before = await Json(await c.GetAsync("/api/sites?pageSize=20&sortBy=Score&descending=true"));
        var (id, rules, rev) = await NewDraftAsync(c, "Fails");
        await SaveAsync(c, id, Edit(rules, "exit", x => x with { MaxPoints = 40 }), rev);
        var hash = (await Json(await c.PostAsync($"/api/methodology/versions/{id}/preview?topN=5", null))).GetProperty("rulesHash").GetString();

        f.Faults.FailNextActivation = true;
        var r = await c.PostAsJsonAsync($"/api/methodology/versions/{id}/publish", new { confirm = true, expectedActiveVersionId = active, rulesHash = hash });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, r.StatusCode);
        var body = await r.Content.ReadAsStringAsync();
        Assert.Contains("methodology_failed", body);
        Assert.DoesNotContain("Injected", body);
        Assert.DoesNotContain("Exception", body);

        Assert.Equal(active, await ActiveIdAsync());
        Assert.Equal(before.GetRawText(), (await Json(await c.GetAsync("/api/sites?pageSize=20&sortBy=Score&descending=true"))).GetRawText());
        Assert.DoesNotContain(await ChangesAsync(id), e => e.GetProperty("action").GetString() == "Publish");
        Assert.Equal("Draft", (await Json(await c.GetAsync($"/api/methodology/versions/{id}"))).GetProperty("version").GetProperty("status").GetString());
    }

    // ------------------------------------------------------------------------------------------------------------

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        var text = await r.Content.ReadAsStringAsync();
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode} {r.RequestMessage?.RequestUri}: {text}");
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private async Task<int> ActiveIdAsync() =>
        (await Json(await Employee_.GetAsync("/api/methodology/active"))).GetProperty("version").GetProperty("id").GetInt32();

    private async Task<List<JsonElement>> ChangesAsync(int versionId) =>
        (await Json(await Employee_.GetAsync($"/api/methodology/changes?versionId={versionId}&pageSize=100"))).GetProperty("entries").EnumerateArray().ToList();

    private static async Task<(int Id, MethodologyRules Rules, int Revision)> NewDraftAsync(HttpClient c, string name)
    {
        var d = await Json(await c.PostAsJsonAsync("/api/methodology/drafts", new { name }));
        return (d.GetProperty("version").GetProperty("id").GetInt32(),
            JsonSerializer.Deserialize<MethodologyRules>(d.GetProperty("rules").GetRawText(), J)!,
            d.GetProperty("version").GetProperty("revision").GetInt32());
    }

    private static Task<HttpResponseMessage> SaveAsync(HttpClient c, int id, MethodologyRules rules, int expectedRevision) =>
        c.PutAsJsonAsync($"/api/methodology/drafts/{id}", new { name = $"draft {id}", changeSummary = "test", rules, expectedRevision }, J);

    /// <summary>Sends rules as raw JSON so values System.Text.Json would refuse to write (NaN) still reach the server.</summary>
    private static Task<HttpResponseMessage> SaveRawAsync(HttpClient c, int id, MethodologyRules rules, int expectedRevision)
    {
        var o = new JsonSerializerOptions(J) { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
        var json = JsonSerializer.Serialize(new { name = $"draft {id}", rules, expectedRevision }, o);
        return c.PutAsync($"/api/methodology/drafts/{id}", new StringContent(json, System.Text.Encoding.UTF8, "application/json"));
    }

    private async Task RestoreAsync(int versionId, int currentlyActive)
    {
        var p = await Json(await Employee_.PostAsync($"/api/methodology/versions/{versionId}/preview?topN=5", null));
        var r = await Employee_.PostAsJsonAsync($"/api/methodology/versions/{versionId}/publish",
            new { confirm = true, expectedActiveVersionId = currentlyActive, rulesHash = p.GetProperty("rulesHash").GetString() });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    private static MethodologyRules Edit(MethodologyRules r, string key, Func<Criterion, Criterion> change) =>
        r with { Criteria = [.. r.Criteria.Select(c => c.Key == key ? change(c) : c)] };

    private static JsonElement Port(JsonElement rules) =>
        rules.GetProperty("criteria").EnumerateArray().Single(c => c.GetProperty("key").GetString() == "port");
}
