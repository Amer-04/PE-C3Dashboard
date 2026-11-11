using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NJTruckCharging.Application.Abstractions;
using NJTruckCharging.Application.Methodology;
using NJTruckCharging.Domain.Methodology;

namespace NJTruckCharging.Tests.Integration;

public sealed class CustomFieldWebAppFactory : TestWebAppFactory
{
    protected override string DatabaseName => "NJTruckCharging_CustomFieldTest";
}

/// <summary>
/// "Other — Custom Criterion": user-named, typed custom data fields (manual per-site values with evidence, or a
/// dataset imported by Site ID), scored only through typed values, with missing values staying Unknown.
/// </summary>
public class CustomFieldApiTests(CustomFieldWebAppFactory f) : IClassFixture<CustomFieldWebAppFactory>, IAsyncLifetime
{
    private static readonly JsonSerializerOptions J = MethodologyJson.Options;
    private HttpClient C => f.CreateClient();

    public async Task InitializeAsync()
    {
        await f.EnsureSeededAsync();
        var baselineId = (await Json(await C.GetAsync("/api/methodology/versions"))).EnumerateArray().Single(v => v.GetProperty("isBaseline").GetBoolean()).GetProperty("id").GetInt32();
        var active = (await Json(await C.GetAsync("/api/methodology/active"))).GetProperty("version").GetProperty("id").GetInt32();
        if (active != baselineId) await PublishAsync(baselineId);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Manual_custom_criterion_unknown_valid_and_invalid_values_through_the_full_workflow()
    {
        var field = await CreateFieldAsync(new { label = "Utility capacity (manual) " + Guid.NewGuid().ToString("N")[..6], description = "Capacity confirmed by the utility",
            type = "Number", source = "Manual", unit = "kW", minValue = 0, maxValue = 20000 });
        var key = field.GetProperty("key").GetString()!;
        Assert.StartsWith("cf", key);

        // A freely named criterion over the typed field; Unknown sites get an explicit −3.
        var (draftId, rules) = await NewDraftAsync();
        rules = rules with { Criteria = [.. rules.Criteria, new Criterion
        {
            Key = "capacityBonus", Name = "Grid capacity ≥ 1 MW (any name is fine)", Category = CriterionCategory.Bonus, Kind = CriterionKind.Cases,
            Cases = [new ScoreCase { Points = 8, Conditions = [new Condition { Field = key, Op = ConditionOp.Gte, Value = 1000 }] }],
            Missing = new MissingDataPolicy { Mode = MissingDataMode.FixedPoints, Points = -3 }
        }] };
        var saved = await SaveAsync(draftId, rules);
        Assert.Contains(saved.GetProperty("criteria").EnumerateArray(), c => c.GetProperty("key").GetString() == "capacityBonus" && c.GetProperty("usesMissableData").GetBoolean());

        var p1 = await PreviewAsync(draftId);
        Assert.Equal(TestWebAppFactory.SeededSiteCount, Impact(p1, "capacityBonus").GetProperty("sitesMissingData").GetInt32());

        var site1 = "TEST-0001"; var site2 = "TEST-0002"; var site3 = "TEST-0003";
        // Site Details: everything starts Unknown.
        var before = await Json(await C.GetAsync($"/api/custom-fields/sites/{site1}"));
        Assert.Equal("Unknown", before.EnumerateArray().Single(v => v.GetProperty("fieldKey").GetString() == key).GetProperty("status").GetString());

        // Invalid values are rejected; nothing is coerced or invented.
        Assert.Equal(HttpStatusCode.BadRequest, (await Put(key, site1, "lots", "letter")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Put(key, site1, "-5", "letter")).StatusCode);         // below min
        Assert.Equal(HttpStatusCode.BadRequest, (await Put(key, site1, "99999", "letter")).StatusCode);      // above max
        Assert.Equal(HttpStatusCode.BadRequest, (await Put(key, site1, "", "letter")).StatusCode);           // empty
        Assert.Equal(HttpStatusCode.BadRequest, (await Put(key, site1, "1500", "  ")).StatusCode);           // evidence required
        Assert.Equal(HttpStatusCode.NotFound, (await Put(key, "NO-SUCH-SITE", "1500", "letter")).StatusCode);

        // Valid values with source evidence.
        var ok = await Json(await Put(key, site1, "1500", "PSE&G capacity letter 2026-10-12"));
        Assert.Equal("Recorded", ok.GetProperty("status").GetString());
        Assert.Equal("1500 kW", ok.GetProperty("display").GetString());
        await Json(await Put(key, site2, "400", "Utility call notes"));

        // Recording values does NOT change live scores (no automatic recalculation)...
        var live1 = await ScoreOf(site1);
        var p2 = await PreviewAsync(draftId);
        Assert.Equal(TestWebAppFactory.SeededSiteCount - 2, Impact(p2, "capacityBonus").GetProperty("sitesMissingData").GetInt32());
        Assert.Equal(live1, await ScoreOf(site1));

        // ...until the draft is previewed and published.
        await PublishAsync(draftId);
        var detail = await DetailAsync(site1);
        var line = detail.GetProperty("scoreLines").EnumerateArray().Single(l => l.GetProperty("key").GetString() == "capacityBonus");
        Assert.Equal(8, line.GetProperty("points").GetDouble());
        Assert.Contains("1,500 kW", line.GetProperty("basis").GetString());
        var line2 = (await DetailAsync(site2)).GetProperty("scoreLines").EnumerateArray().Single(l => l.GetProperty("key").GetString() == "capacityBonus");
        Assert.Equal(0, line2.GetProperty("points").GetDouble());
        var line3 = (await DetailAsync(site3)).GetProperty("scoreLines").EnumerateArray().Single(l => l.GetProperty("key").GetString() == "capacityBonus");
        Assert.Equal(-3, line3.GetProperty("points").GetDouble());
        Assert.Contains("no data", line3.GetProperty("basis").GetString());
        await AssertLiveMatchesEngineAsync(rules);

        // Clearing a value returns the site to Unknown.
        Assert.Equal(HttpStatusCode.NoContent, (await C.DeleteAsync($"/api/custom-fields/{key}/sites/{site2}")).StatusCode);
        var after = await Json(await C.GetAsync($"/api/custom-fields/sites/{site2}"));
        Assert.Equal("Unknown", after.EnumerateArray().Single(v => v.GetProperty("fieldKey").GetString() == key).GetProperty("status").GetString());

        // The field definition's type is fixed once created.
        var change = await C.PutAsJsonAsync($"/api/custom-fields/{key}", new { label = "x", type = "Boolean", source = "Manual" });
        Assert.Equal(HttpStatusCode.Conflict, change.StatusCode);
        var list = await Json(await C.GetAsync("/api/custom-fields"));
        var dto = list.EnumerateArray().Single(x => x.GetProperty("key").GetString() == key);
        Assert.True(dto.GetProperty("usedByActive").GetBoolean());
        Assert.Equal(1, dto.GetProperty("valueCount").GetInt32());
    }

    [Fact]
    public async Task Imported_custom_field_validates_previews_and_commits_by_site_id()
    {
        var field = await CreateFieldAsync(new { label = "Zoning status (import) " + Guid.NewGuid().ToString("N")[..6], type = "Category", source = "Imported",
            categories = new[] { "Permitted", "Conditional", "Prohibited" } });
        var key = field.GetProperty("key").GetString()!;

        // Manual entry is not allowed for imported fields.
        Assert.Equal(HttpStatusCode.Conflict, (await Put(key, "TEST-0001", "Permitted", "x")).StatusCode);

        // A file with every kind of problem: unknown site, duplicate, invalid category, empty value, missing site_id.
        var bad = "site_id,value,evidence\nTEST-0001,Permitted,county map\nNO-SUCH,Permitted,\nTEST-0001,Conditional,\nTEST-0002,Industrial,\nTEST-0003,,\n,Permitted,\n";
        var pBad = await Json(await C.PostAsJsonAsync($"/api/custom-fields/{key}/import/preview", new { fileName = "bad.csv", csv = bad }));
        Assert.False(pBad.GetProperty("canCommit").GetBoolean());
        Assert.Equal(1, pBad.GetProperty("validRows").GetInt32());
        Assert.Equal(1, pBad.GetProperty("unknownSiteIds").GetInt32());
        Assert.Equal(1, pBad.GetProperty("duplicates").GetInt32());
        Assert.Equal(3, pBad.GetProperty("invalidValues").GetInt32());
        Assert.Equal(5, pBad.GetProperty("errors").GetArrayLength());
        var badCommit = await C.PostAsJsonAsync($"/api/custom-fields/{key}/import/commit",
            new { fileName = "bad.csv", csv = bad, token = pBad.GetProperty("token").GetString() });
        Assert.Equal(HttpStatusCode.BadRequest, badCommit.StatusCode);
        Assert.Equal(0, (await FieldDto(key)).GetProperty("valueCount").GetInt32());   // nothing written

        // Header problems are reported clearly.
        var noHeader = await C.PostAsJsonAsync($"/api/custom-fields/{key}/import/preview", new { fileName = "x.csv", csv = "TEST-0001,Permitted\n" });
        Assert.Equal(HttpStatusCode.BadRequest, noHeader.StatusCode);

        // A clean file: preview → commit with the preview token.
        var good = "Site ID,value,evidence\nTEST-0001,permitted,\"County zoning map, 2026\"\nTEST-0002,Conditional,Planning board minutes\nTEST-0004,Prohibited,Ordinance 12-4\n";
        var pGood = await Json(await C.PostAsJsonAsync($"/api/custom-fields/{key}/import/preview", new { fileName = "zoning.csv", csv = good }));
        Assert.True(pGood.GetProperty("canCommit").GetBoolean());
        Assert.Equal(3, pGood.GetProperty("inserts").GetInt32());
        Assert.Equal(TestWebAppFactory.SeededSiteCount - 3, pGood.GetProperty("sitesWithoutValueAfter").GetInt32());
        var wrongToken = await C.PostAsJsonAsync($"/api/custom-fields/{key}/import/commit", new { fileName = "zoning.csv", csv = good, token = "nope" });
        Assert.Equal(HttpStatusCode.Conflict, wrongToken.StatusCode);
        var committed = await Json(await C.PostAsJsonAsync($"/api/custom-fields/{key}/import/commit",
            new { fileName = "zoning.csv", csv = good, token = pGood.GetProperty("token").GetString() }));
        Assert.Equal(3, committed.GetProperty("inserted").GetInt32());

        var v1 = (await Json(await C.GetAsync("/api/custom-fields/sites/TEST-0001"))).EnumerateArray().Single(v => v.GetProperty("fieldKey").GetString() == key);
        Assert.Equal("Permitted", v1.GetProperty("categoryValue").GetString());            // canonical casing
        Assert.Equal("County zoning map, 2026", v1.GetProperty("evidence").GetString());
        Assert.Contains("zoning.csv", v1.GetProperty("importBatch").GetString());

        // Re-previewing the same file shows nothing to change.
        var again = await Json(await C.PostAsJsonAsync($"/api/custom-fields/{key}/import/preview", new { fileName = "zoning.csv", csv = good }));
        Assert.Equal(3, again.GetProperty("unchanged").GetInt32());

        // A criterion on the imported category scores through the normal preview.
        var (draftId, rules) = await NewDraftAsync();
        rules = rules with { Criteria = [.. rules.Criteria, new Criterion
        {
            Key = "zoningPenalty", Name = "Zoning prohibits use", Category = CriterionCategory.Penalty, Kind = CriterionKind.Cases,
            Cases = [new ScoreCase { Points = -20, Conditions = [new Condition { Field = key, Op = ConditionOp.In, Values = ["Prohibited"] }] }]
        }] };
        await SaveAsync(draftId, rules);
        var preview = await PreviewAsync(draftId);
        var impact = Impact(preview, "zoningPenalty");
        Assert.Equal(1, impact.GetProperty("sitesNonZero").GetInt32());
        Assert.Equal(TestWebAppFactory.SeededSiteCount - 3, impact.GetProperty("sitesMissingData").GetInt32());
        var rev = (await Json(await C.GetAsync($"/api/methodology/versions/{draftId}"))).GetProperty("version").GetProperty("revision").GetInt32();
        Assert.Equal(HttpStatusCode.NoContent, (await C.DeleteAsync($"/api/methodology/drafts/{draftId}?revision={rev}")).StatusCode);

        // Removing a category that recorded values use is refused.
        var drop = await C.PutAsJsonAsync($"/api/custom-fields/{key}", new { label = "Zoning", type = "Category", source = "Imported", categories = new[] { "Permitted", "Conditional" } });
        Assert.Equal(HttpStatusCode.Conflict, drop.StatusCode);
    }

    [Fact]
    public async Task Custom_field_definitions_are_validated_and_writes_are_protected()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await C.PostAsJsonAsync("/api/custom-fields", new { label = "", type = "Number", source = "Manual" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await C.PostAsJsonAsync("/api/custom-fields", new { label = "Bad bounds", type = "Number", source = "Manual", minValue = 10, maxValue = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await C.PostAsJsonAsync("/api/custom-fields", new { label = "One cat", type = "Category", source = "Manual", categories = new[] { "A" } })).StatusCode);

        var outsider = f.CreateClientFrom("203.0.113.9");
        Assert.Equal(HttpStatusCode.OK, (await outsider.GetAsync("/api/custom-fields")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.PostAsJsonAsync("/api/custom-fields", new { label = "x", type = "Number", source = "Manual" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.PutAsJsonAsync("/api/custom-fields/cfAnything/sites/TEST-0001", new { value = "1", evidence = "e" })).StatusCode);
        var cross = new HttpRequestMessage(HttpMethod.Post, "/api/custom-fields") { Content = JsonContent.Create(new { label = "x", type = "Number", source = "Manual" }) };
        cross.Headers.Add("Origin", "https://evil.example");
        Assert.Equal(HttpStatusCode.Forbidden, (await C.SendAsync(cross)).StatusCode);

        // A free-text name can never become code: it is only a label; the key is a safe identifier.
        var f2 = await CreateFieldAsync(new { label = "x'); DROP TABLE CandidateSites; -- " + Guid.NewGuid().ToString("N")[..4], type = "Boolean", source = "Manual" });
        Assert.Matches("^cf[A-Za-z0-9]+$", f2.GetProperty("key").GetString()!);
        Assert.Equal(TestWebAppFactory.SeededSiteCount, (await Json(await C.GetAsync("/api/sites?page=1&pageSize=1"))).GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Custom_values_survive_a_reimport_and_the_active_methodology_is_reapplied_with_them()
    {
        var field = await CreateFieldAsync(new { label = "Site visit done " + Guid.NewGuid().ToString("N")[..6], type = "Boolean", source = "Manual" });
        var key = field.GetProperty("key").GetString()!;
        await Json(await Put(key, "TEST-0005", "yes", "Visited 2026-11-01"));
        var (draftId, rules) = await NewDraftAsync();
        rules = rules with { Criteria = [.. rules.Criteria, new Criterion
        {
            Key = "visited", Name = "Site visited", Category = CriterionCategory.Bonus, Kind = CriterionKind.Cases,
            Cases = [new ScoreCase { Points = 4, Conditions = [new Condition { Field = key, Op = ConditionOp.IsTrue }] }]
        }] };
        await SaveAsync(draftId, rules);
        await PublishAsync(draftId);
        var before = await ScoreOf("TEST-0005");

        await f.SimulateReimportAsync();
        Assert.Equal(HttpStatusCode.OK, (await C.PostAsync("/api/admin/rescore", null)).StatusCode);
        Assert.Equal(before, await ScoreOf("TEST-0005"));
        var v = (await Json(await C.GetAsync("/api/custom-fields/sites/TEST-0005"))).EnumerateArray().Single(x => x.GetProperty("fieldKey").GetString() == key);
        Assert.Equal("Recorded", v.GetProperty("status").GetString());
        await AssertLiveMatchesEngineAsync(rules);
    }

    // ---- helpers ----------------------------------------------------------------------------------------------

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        var body = await r.Content.ReadAsStringAsync();
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode} {r.RequestMessage?.RequestUri}: {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private Task<HttpResponseMessage> Put(string key, string siteId, string value, string? evidence) =>
        C.PutAsJsonAsync($"/api/custom-fields/{key}/sites/{Uri.EscapeDataString(siteId)}", new { value, evidence });

    private async Task<JsonElement> CreateFieldAsync(object body) => await Json(await C.PostAsJsonAsync("/api/custom-fields", body));

    private async Task<JsonElement> FieldDto(string key) =>
        (await Json(await C.GetAsync("/api/custom-fields"))).EnumerateArray().Single(x => x.GetProperty("key").GetString() == key);

    private async Task<(int Id, MethodologyRules Rules)> NewDraftAsync()
    {
        var d = await Json(await C.PostAsJsonAsync("/api/methodology/drafts", new { name = "custom " + Guid.NewGuid().ToString("N")[..6] }));
        return (d.GetProperty("version").GetProperty("id").GetInt32(), JsonSerializer.Deserialize<MethodologyRules>(d.GetProperty("rules").GetRawText(), J)!);
    }

    private async Task<JsonElement> SaveAsync(int id, MethodologyRules rules)
    {
        var rev = (await Json(await C.GetAsync($"/api/methodology/versions/{id}"))).GetProperty("version").GetProperty("revision").GetInt32();
        return await Json(await C.PutAsJsonAsync($"/api/methodology/drafts/{id}", new { name = $"custom draft {id}", changeSummary = "test", rules, expectedRevision = rev }, J));
    }

    private async Task<JsonElement> PreviewAsync(int id) => await Json(await C.PostAsync($"/api/methodology/versions/{id}/preview?topN=5", null));

    private async Task PublishAsync(int id)
    {
        var p = await PreviewAsync(id);
        await Json(await C.PostAsJsonAsync($"/api/methodology/versions/{id}/publish", new
        {
            confirm = true, expectedActiveVersionId = p.GetProperty("comparedToVersionId").GetInt32(), rulesHash = p.GetProperty("rulesHash").GetString()
        }));
    }

    private static JsonElement Impact(JsonElement preview, string key) =>
        preview.GetProperty("criteria").EnumerateArray().Single(c => c.GetProperty("key").GetString() == key);

    private async Task<JsonElement> DetailAsync(string siteId)
    {
        var item = (await Json(await C.GetAsync($"/api/sites?search={siteId}&page=1&pageSize=5"))).GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("siteId").GetString() == siteId);
        return await Json(await C.GetAsync($"/api/sites/{item.GetProperty("id").GetInt32()}"));
    }

    private async Task<double> ScoreOf(string siteId) => (await DetailAsync(siteId)).GetProperty("summary").GetProperty("score").GetDouble();

    private async Task AssertLiveMatchesEngineAsync(MethodologyRules rules)
    {
        using var scope = f.Services.CreateScope();
        var sites = await scope.ServiceProvider.GetRequiredService<IMethodologyStore>().LoadSitesForScoringAsync();
        var catalog = await CustomCatalog.BuildAsync(scope.ServiceProvider.GetRequiredService<ICustomFieldStore>());
        var expected = MethodologyEngine.Evaluate(sites, rules, catalog).Sites.ToDictionary(r => r.SiteId, r => (r.Score, r.Rank));
        var live = new Dictionary<string, (double, int)>();
        for (var page = 1; ; page++)
        {
            var items = (await Json(await C.GetAsync($"/api/sites?page={page}&pageSize=500"))).GetProperty("items").EnumerateArray().ToList();
            foreach (var i in items) live[i.GetProperty("siteId").GetString()!] = (i.GetProperty("score").GetDouble(), i.GetProperty("rank").GetInt32());
            if (items.Count < 500) break;
        }
        Assert.Equal(expected.Count, live.Count);
        Assert.All(expected, kv => Assert.Equal(kv.Value, live[kv.Key]));
    }
}
