using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NJTruckCharging.Infrastructure.Persistence;
using NJTruckCharging.Security;

namespace NJTruckCharging.Tests.Integration;

/// <summary>
/// Final safety checks for shared methodology editing: existing user data is never touched by publishing, the read-only
/// exception cannot be widened by request tricks, every admitted user type can edit, and every change is fully audited.
/// </summary>
public partial class MethodologyCloudEditingApiTests
{
    // ------------------------------------------------------------------------------------------------------------
    // Existing data
    // ------------------------------------------------------------------------------------------------------------

    /// <summary>Every table that is not methodology data; CandidateSites is compared on its non-score columns.</summary>
    private static readonly string[] UserTables =
    [
        "SavedSites", "SiteContacts", "FeasibilityItems", "SiteScreenings", "SiteScreeningEntries", "ResearchHistoryEntries",
        "CustomDataFields", "CustomFieldValues", "NearbyFleets", "Segments", "PowerEdisonSites", "Interchanges", "MethodologyNotes",
    ];

    private const string SeedSql = """
        INSERT INTO "SavedSites" ("SiteId", "Notes", "CreatedAtUtc", "UpdatedAtUtc")
            SELECT TOP 3 "SiteId", 'regression bookmark', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP FROM "CandidateSites" ORDER BY "Score" DESC;
        INSERT INTO "SiteContacts" ("SiteId", "Name", "ContactType", "VerificationStatus", "SourceUrl", "CreatedAtUtc", "UpdatedAtUtc")
            SELECT TOP 2 "SiteId", 'Regression Owner', 'PropertyOwner', 'Unverified', 'https://example.org/owner', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP FROM "CandidateSites" ORDER BY "Score" DESC;
        INSERT INTO "FeasibilityItems" ("SiteId", "ItemKey", "Status", "Evidence", "CreatedAtUtc", "UpdatedAtUtc")
            SELECT TOP 2 "SiteId", 'Zoning', 'Unknown', 'regression', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP FROM "CandidateSites" ORDER BY "Score" DESC;
        INSERT INTO "SiteScreenings" ("SiteId", "Decision", "Evidence", "CreatedAtUtc", "UpdatedAtUtc")
            SELECT TOP 2 "SiteId", 'Promising', 'regression evidence', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP FROM "CandidateSites" ORDER BY "Score" DESC;
        INSERT INTO "SiteScreeningEntries" ("SiteId", "Decision", "Evidence", "RecordedAtUtc")
            SELECT TOP 2 "SiteId", 'Promising', 'regression evidence', CURRENT_TIMESTAMP FROM "CandidateSites" ORDER BY "Score" DESC;
        INSERT INTO "ResearchHistoryEntries" ("SiteId", "Subject", "Action", "Note", "RecordedAtUtc")
            SELECT TOP 2 "SiteId", 'Site', 'Started', 'regression', CURRENT_TIMESTAMP FROM "CandidateSites" ORDER BY "Score" DESC;
        INSERT INTO "CustomDataFields" ("Key", "Label", "Type", "Source", "Categories", "CreatedAtUtc", "UpdatedAtUtc")
            VALUES ('cfRegression', 'Regression zoning', 'Category', 'Manual', 'Permitted,Conditional', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);
        INSERT INTO "CustomFieldValues" ("FieldKey", "SiteId", "CategoryValue", "Evidence", "Source", "CreatedAtUtc", "UpdatedAtUtc")
            SELECT TOP 2 'cfRegression', "SiteId", 'Permitted', 'regression', 'Manual', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP FROM "CandidateSites" ORDER BY "Score" DESC;
        INSERT INTO "NearbyFleets" ("CandidateSiteId", "Segment", "RankInSegment", "Carrier", "Trucks", "Tractors", "Drivers", "ApproxMiles")
            SELECT TOP 2 "Id", "Segment", "RankInSegment", 'Regression Carrier', 12, 8, 15, 1.5 FROM "CandidateSites" ORDER BY "Score" DESC;
        """;

    private const string CleanupSql = """
        DELETE FROM "NearbyFleets" WHERE "Carrier" = 'Regression Carrier';
        DELETE FROM "CustomFieldValues" WHERE "FieldKey" = 'cfRegression';
        DELETE FROM "CustomDataFields" WHERE "Key" = 'cfRegression';
        DELETE FROM "ResearchHistoryEntries" WHERE "Note" = 'regression';
        DELETE FROM "SiteScreeningEntries" WHERE "Evidence" = 'regression evidence';
        DELETE FROM "SiteScreenings" WHERE "Evidence" = 'regression evidence';
        DELETE FROM "FeasibilityItems" WHERE "Evidence" = 'regression';
        DELETE FROM "SiteContacts" WHERE "Name" = 'Regression Owner';
        DELETE FROM "SavedSites" WHERE "Notes" = 'regression bookmark';
        """;

    /// <summary>Every row of every user table (as JSON), plus all non-score columns of every candidate site.</summary>
    private async Task<Dictionary<string, string>> SnapshotUserDataAsync()
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync();
        try
        {
            async Task<string> Json(string sql)
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;
                var sb = new System.Text.StringBuilder();
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync()) if (!r.IsDBNull(0)) sb.Append(r.GetString(0));
                return sb.ToString();
            }
            var result = new Dictionary<string, string>();
            foreach (var t in UserTables)
                result[t] = await Json($"SELECT (SELECT * FROM [{t}] ORDER BY 1 FOR JSON PATH, INCLUDE_NULL_VALUES)");
            var cols = new List<string>();
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = """
                    SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'CandidateSites'
                      AND COLUMN_NAME NOT IN ('Score', 'ComputedRank', 'TruckDemandPoints', 'ScoreVersionId') AND COLUMN_NAME NOT LIKE 'Breakdown[_]%'
                    ORDER BY ORDINAL_POSITION
                    """;
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync()) cols.Add($"[{r.GetString(0)}]");
            }
            result["CandidateSites (non-score columns)"] =
                await Json($"SELECT (SELECT {string.Join(",", cols)} FROM [CandidateSites] ORDER BY [Id] FOR JSON PATH, INCLUDE_NULL_VALUES)");
            return result;
        }
        finally { await conn.CloseAsync(); }
    }

    private async Task ExecAsync(string sql)
    {
        using var scope = f.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.ExecuteSqlRawAsync(sql);
    }

    [Fact]
    public async Task Publishing_and_restoring_never_modify_saved_sites_contacts_research_or_any_other_existing_data()
    {
        await ExecAsync(CleanupSql);
        await ExecAsync(SeedSql);
        try
        {
            var before = await SnapshotUserDataAsync();
            Assert.All(new[] { "SavedSites", "SiteContacts", "FeasibilityItems", "SiteScreenings", "SiteScreeningEntries",
                "ResearchHistoryEntries", "CustomDataFields", "CustomFieldValues", "NearbyFleets" },
                t => Assert.False(string.IsNullOrEmpty(before[t]), $"{t} was not populated by the seed"));

            var c = Employee_;
            var active = await ActiveIdAsync();
            var (id, rules, rev) = await NewDraftAsync(c, "Regression: data untouched");
            await SaveAsync(c, id, Edit(rules, "openLand", x => x with { Cases = [.. x.Cases.Select(k => k with { Points = k.Points + 3 })] }), rev);
            var hash = (await Json(await c.PostAsync($"/api/methodology/versions/{id}/preview?topN=5", null))).GetProperty("rulesHash").GetString();
            Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync($"/api/methodology/versions/{id}/publish",
                new { confirm = true, expectedActiveVersionId = active, rulesHash = hash })).StatusCode);
            var afterPublish = await SnapshotUserDataAsync();
            await RestoreAsync(active, id);
            var afterRestore = await SnapshotUserDataAsync();

            foreach (var (table, json) in before)
            {
                Assert.True(json == afterPublish[table], $"{table} changed when scoring was applied");
                Assert.True(json == afterRestore[table], $"{table} changed when the previous version was restored");
            }
            // The bookmarks, contacts and decisions are still served unchanged by the API.
            var saved = await Json(await c.GetAsync("/api/saved-sites"));
            Assert.Equal(3, saved.EnumerateArray().Count(s => s.GetProperty("notes").GetString() == "regression bookmark"));
        }
        finally { await ExecAsync(CleanupSql); }
    }

    // ------------------------------------------------------------------------------------------------------------
    // The read-only exception cannot be widened
    // ------------------------------------------------------------------------------------------------------------

    /// <summary>At run time, exactly the seven methodology change endpoints carry the editor marker.</summary>
    [Fact]
    public void Only_the_seven_methodology_change_routes_carry_the_editor_marker_at_run_time()
    {
        var marked = f.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<RequireMethodologyEditorAttribute>() is not null)
            .Select(e => $"{string.Join(",", e.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()!.HttpMethods)} {e.RoutePattern.RawText}")
            .OrderBy(x => x).ToList();
        Assert.Equal(new[]
        {
            "DELETE api/methodology/drafts/{id:int}", "POST api/methodology/drafts", "POST api/methodology/validate",
            "POST api/methodology/versions/{id:int}/duplicate", "POST api/methodology/versions/{id:int}/preview",
            "POST api/methodology/versions/{id:int}/publish", "PUT api/methodology/drafts/{id:int}",
        }, marked);
    }

    [Fact]
    public async Task Request_tricks_cannot_reach_other_writes_through_the_methodology_exception()
    {
        var c = Employee_;
        var savedBefore = (await Json(await c.GetAsync("/api/saved-sites"))).GetArrayLength();
        var site = (await Json(await c.GetAsync("/api/sites?pageSize=1"))).GetProperty("items")[0].GetProperty("siteId").GetString();
        var body = JsonContent.Create(new { siteId = site, notes = "bypass attempt" });

        async Task<HttpStatusCode> Send(HttpMethod m, string url, Action<HttpRequestMessage>? tweak = null)
        {
            var req = new HttpRequestMessage(m, url) { Content = m == HttpMethod.Get || m == HttpMethod.Head ? null : JsonContent.Create(new { siteId = site, notes = "bypass attempt" }) };
            tweak?.Invoke(req);
            return (await c.SendAsync(req)).StatusCode;
        }

        var attempts = new List<(string What, HttpStatusCode Status)>
        {
            ("plain", await Send(HttpMethod.Post, "/api/saved-sites")),
            ("upper case", await Send(HttpMethod.Post, "/API/SAVED-SITES")),
            ("trailing slash", await Send(HttpMethod.Post, "/api/saved-sites/")),
            ("methodology path in the query", await Send(HttpMethod.Post, "/api/saved-sites?x=/api/methodology/drafts")),
            ("encoded dot segments from a methodology path", await Send(HttpMethod.Post, "/api/methodology/drafts/%2e%2e/%2e%2e/saved-sites")),
            ("encoded slash", await Send(HttpMethod.Post, "/api/methodology%2fdrafts")),
            ("method override header", await Send(HttpMethod.Post, "/api/saved-sites", r => { r.Headers.Add("X-HTTP-Method-Override", "GET"); r.Headers.Add("X-Method-Override", "GET"); })),
            ("forwarded/original URL headers", await Send(HttpMethod.Post, "/api/saved-sites", r =>
                { r.Headers.Add("X-Original-URL", "/api/methodology/drafts"); r.Headers.Add("X-Rewrite-URL", "/api/methodology/drafts"); r.Headers.Add("X-Forwarded-Prefix", "/api/methodology"); })),
            ("PATCH on a methodology route", await Send(HttpMethod.Patch, "/api/methodology/drafts/1")),
            ("contacts", await Send(HttpMethod.Post, "/api/contacts")),
            ("admin import", await Send(HttpMethod.Post, "/api/admin/import")),
            ("custom field value", await Send(HttpMethod.Put, $"/api/custom-fields/cfAnything/sites/{site}")),
        };
        foreach (var (what, status) in attempts)
            Assert.True((int)status >= 400, $"{what}: expected a refusal, got {(int)status}");
        // OPTIONS/HEAD pass the gate as non-writes but never run a write handler.
        await Send(HttpMethod.Options, "/api/saved-sites");
        await Send(HttpMethod.Head, "/api/saved-sites");
        Assert.Equal(savedBefore, (await Json(await c.GetAsync("/api/saved-sites"))).GetArrayLength());
        body.Dispose();
    }

    // ------------------------------------------------------------------------------------------------------------
    // Who may edit: every admitted user type; nobody else
    // ------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("rules.editor@poweredison.com")]
    [InlineData("second.editor@ev-edison.com")]
    [InlineData("c3.admin.personal@gmail.com")]
    [InlineData("Mixed.Case@PoweredIson.com")]
    public async Task Every_admitted_user_type_can_create_save_review_and_delete_a_draft(string email)
    {
        var id = email switch
        {
            MethodologyEditingWebAppFactory.EmployeeEmail => Employee,
            MethodologyEditingWebAppFactory.ColleagueEmail => Colleague,
            MethodologyEditingWebAppFactory.AdminEmail => Admin,
            _ => f.Users.Add(email),
        };
        if (id == Colleague) Reset(Colleague, MethodologyEditingWebAppFactory.ColleagueEmail);
        if (id == Admin) Reset(Admin, MethodologyEditingWebAppFactory.AdminEmail);
        var c = As(id);
        var (draft, rules, rev) = await NewDraftAsync(c, $"by {email}");
        Assert.Equal(HttpStatusCode.OK, (await SaveAsync(c, draft, Edit(rules, "port", x => x with { DefaultPoints = 1 }), rev)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsync($"/api/methodology/versions/{draft}/preview?topN=5", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/methodology/drafts/{draft}?revision={rev + 1}")).StatusCode);
        Assert.All(await ChangesAsync(draft), e => Assert.Equal(email, e.GetProperty("actorEmail").GetString(), ignoreCase: true));
    }

    [Fact]
    public async Task An_expired_or_forged_token_cannot_edit()
    {
        var expired = f.Token(Employee, expires: DateTime.UtcNow.AddMinutes(-5));
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.ClientWith(expired).PostAsJsonAsync("/api/methodology/drafts", new { name = "x" })).StatusCode);
        var forged = CloudAuthWebAppFactory.MakeToken(Employee, MethodologyEditingWebAppFactory.EmployeeEmail, key: CloudAuthWebAppFactory.NewKey("supabase-test-key"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.ClientWith(forged).PostAsJsonAsync("/api/methodology/drafts", new { name = "x" })).StatusCode);
        var anon = f.Token(Employee, anonymous: true);
        Assert.NotEqual(HttpStatusCode.OK, (await f.ClientWith(anon).PostAsJsonAsync("/api/methodology/drafts", new { name = "x" })).StatusCode);
    }

    // ------------------------------------------------------------------------------------------------------------
    // Every change is audited with e-mail, user id, time and previous/new values
    // ------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Every_kind_of_methodology_change_is_audited_with_editor_time_and_previous_and_new_values()
    {
        var c = Employee_;
        var active = await ActiveIdAsync();
        var (id, rules, rev) = await NewDraftAsync(c, "Audit: full cycle");
        await SaveAsync(c, id, Edit(rules, "exit", x => x with { MaxPoints = 22 }), rev);
        var dup = (await Json(await c.PostAsJsonAsync($"/api/methodology/versions/{id}/duplicate", new { name = "Audit: copy" }))).GetProperty("version");
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/methodology/drafts/{dup.GetProperty("id").GetInt32()}?revision={dup.GetProperty("revision").GetInt32()}")).StatusCode);
        var hash = (await Json(await c.PostAsync($"/api/methodology/versions/{id}/preview?topN=5", null))).GetProperty("rulesHash").GetString();
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync($"/api/methodology/versions/{id}/publish",
            new { confirm = true, expectedActiveVersionId = active, rulesHash = hash })).StatusCode);
        await RestoreAsync(active, id);

        var all = (await Json(await c.GetAsync("/api/methodology/changes?pageSize=100"))).GetProperty("entries").EnumerateArray()
            .Where(e => e.GetProperty("versionId").GetInt32() == id || e.GetProperty("versionId").GetInt32() == dup.GetProperty("id").GetInt32()
                        || (e.GetProperty("versionId").GetInt32() == active && e.GetProperty("previousActiveVersionId").ValueKind == JsonValueKind.Number
                            && e.GetProperty("previousActiveVersionId").GetInt32() == id))
            .ToList();
        var actions = all.Select(e => e.GetProperty("action").GetString()!).ToHashSet();
        Assert.Superset(new HashSet<string> { "CreateDraft", "SaveDraft", "DeleteDraft", "Preview", "Publish", "Restore" }, actions);

        foreach (var e in all)
        {
            var action = e.GetProperty("action").GetString();
            Assert.Equal(Employee.ToString(), e.GetProperty("actorUserId").GetString());
            Assert.Equal(MethodologyEditingWebAppFactory.EmployeeEmail, e.GetProperty("actorEmail").GetString());
            Assert.True(DateTimeOffset.UtcNow - e.GetProperty("atUtc").GetDateTimeOffset() < TimeSpan.FromMinutes(10), $"{action}: timestamp");
            if (action == "Preview") continue;   // a review stores proposed scores only; the rules do not change
            Assert.True(e.GetProperty("hasSnapshots").GetBoolean(), $"{action}: previous/new values recorded");
            var snap = await Json(await c.GetAsync($"/api/methodology/changes/{e.GetProperty("id").GetInt64()}/snapshot"));
            Assert.Equal(JsonValueKind.Object, snap.GetProperty("before").ValueKind);   // previous values
            if (action != "DeleteDraft") Assert.Equal(JsonValueKind.Object, snap.GetProperty("after").ValueKind);   // new values
        }
        var publish = all.Single(e => e.GetProperty("action").GetString() == "Publish");
        var ps = await Json(await c.GetAsync($"/api/methodology/changes/{publish.GetProperty("id").GetInt64()}/snapshot"));
        double Exit(JsonElement s) => s.GetProperty("rules").GetProperty("criteria").EnumerateArray().Single(x => x.GetProperty("key").GetString() == "exit").GetProperty("maxPoints").GetDouble();
        Assert.NotEqual(22, Exit(ps.GetProperty("before")));
        Assert.Equal(22, Exit(ps.GetProperty("after")));
    }
}
