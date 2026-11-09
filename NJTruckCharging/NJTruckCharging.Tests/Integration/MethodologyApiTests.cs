using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NJTruckCharging.Application.Abstractions;
using NJTruckCharging.Domain.Methodology;
using NJTruckCharging.Infrastructure.Persistence;

namespace NJTruckCharging.Tests.Integration;

public sealed class MethodologyWebAppFactory : TestWebAppFactory
{
    protected override string DatabaseName => "NJTruckCharging_MethodologyTest";
}

/// <summary>
/// Editable methodology end-to-end through the HTTP API on a dedicated LocalDB database (1,500 seeded sites):
/// draft → validate → preview (all sites) → confirm → atomic publish → every view consistent → restore.
/// Every test starts (and leaves the next test starting) from the baseline Version 1.
/// </summary>
public class MethodologyApiTests(MethodologyWebAppFactory f) : IClassFixture<MethodologyWebAppFactory>, IAsyncLifetime
{
    private static readonly JsonSerializerOptions J = MethodologyJson.Options;
    private HttpClient Admin => f.CreateTrustedClient();
    private HttpClient Anon => f.CreateClient();

    public async Task InitializeAsync()
    {
        await f.EnsureSeededAsync();
        await f.ClearSavedAsync();
        await f.ClearScreeningAsync();
        f.Faults.FailNextActivation = false;
        await RestoreBaselineAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // =============================================================================================================
    // Security + immutability
    // =============================================================================================================

    [Fact]
    public async Task Methodology_writes_are_limited_to_the_trusted_network_and_same_origin_without_any_browser_credential()
    {
        var baselineId = await BaselineIdAsync();
        // Reads are open to everyone.
        var outsider = f.CreateClientFrom("203.0.113.7");
        Assert.Equal(HttpStatusCode.OK, (await outsider.GetAsync("/api/methodology/active")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await outsider.GetAsync("/api/methodology/versions")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await outsider.GetAsync("/api/methodology/fields")).StatusCode);

        // Writes from outside the trusted network are refused (no key can unlock them).
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.PostAsJsonAsync("/api/methodology/drafts", new { name = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.PostAsync($"/api/methodology/versions/{baselineId}/preview", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.PostAsJsonAsync($"/api/methodology/versions/{baselineId}/publish",
            new { confirm = true, expectedActiveVersionId = baselineId, rulesHash = "x" })).StatusCode);
        var legacyKey = f.CreateClientFrom("203.0.113.7");
        legacyKey.DefaultRequestHeaders.Add("X-Admin-Key", "anything");
        Assert.Equal(HttpStatusCode.Forbidden, (await legacyKey.PostAsJsonAsync("/api/methodology/drafts", new { name = "x" })).StatusCode);

        // A trusted host is refused when the request is driven by another web site (CSRF).
        var crossSite = new HttpRequestMessage(HttpMethod.Post, "/api/methodology/drafts") { Content = JsonContent.Create(new { name = "x" }) };
        crossSite.Headers.Add("Origin", "https://evil.example");
        Assert.Equal(HttpStatusCode.Forbidden, (await Admin.SendAsync(crossSite)).StatusCode);
        var fetchSite = new HttpRequestMessage(HttpMethod.Post, "/api/methodology/drafts") { Content = JsonContent.Create(new { name = "x" }) };
        fetchSite.Headers.Add("Sec-Fetch-Site", "cross-site");
        Assert.Equal(HttpStatusCode.Forbidden, (await Admin.SendAsync(fetchSite)).StatusCode);

        // The dashboard origin on the trusted network may create a draft — no credential needed.
        var fromDashboard = new HttpRequestMessage(HttpMethod.Post, "/api/methodology/drafts") { Content = JsonContent.Create(new { name = "ok" }) };
        fromDashboard.Headers.Add("Origin", "http://localhost:4200");
        var ok = await Admin.SendAsync(fromDashboard);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Admin.DeleteAsync($"/api/methodology/drafts/{(await Json(ok)).GetProperty("version").GetProperty("id").GetInt32()}")).StatusCode);
    }

    [Fact]
    public async Task Scoring_loader_projection_returns_every_catalog_field_unchanged()
    {
        using var scope = f.Services.CreateScope();
        var projected = await scope.ServiceProvider.GetRequiredService<IMethodologyStore>().LoadSitesForScoringAsync();
        var full = await scope.ServiceProvider.GetRequiredService<AppDbContext>().CandidateSites.AsNoTracking().ToDictionaryAsync(s => s.SiteId);
        Assert.Equal(full.Count, projected.Count);
        foreach (var p in projected)
        {
            var s = full[p.SiteId];
            foreach (var field in ScoringFieldCatalog.All)
                Assert.True(field.Read(s) == field.Read(p) || (field.Read(s).Lots ?? []).SequenceEqual(field.Read(p).Lots ?? []),
                    $"{field.Key} differs for {p.SiteId}");
            Assert.Equal((s.Score, s.ComputedRank, s.MilesToExit, s.RankInSegment, s.SiteAddress), (p.Score, p.ComputedRank, p.MilesToExit, p.RankInSegment, p.SiteAddress));
        }
        Assert.Equal(projected.OrderBy(x => x.Segment).ThenBy(x => x.SiteId, StringComparer.Ordinal).Select(x => x.SiteId), projected.Select(x => x.SiteId));
    }

    [Fact]
    public async Task Baseline_version_1_is_immutable_and_is_the_initial_active_version()
    {
        var active = await ActiveAsync();
        var baselineId = await BaselineIdAsync();
        Assert.Equal(baselineId, active.GetProperty("version").GetProperty("id").GetInt32());
        Assert.True(active.GetProperty("version").GetProperty("isBaseline").GetBoolean());
        Assert.Equal(16, active.GetProperty("criteria").GetArrayLength());

        var rules = BaselineMethodology.Create();
        var put = await Admin.PutAsJsonAsync($"/api/methodology/drafts/{baselineId}", new { name = "hack", rules }, J);
        Assert.Equal(HttpStatusCode.Conflict, put.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Admin.DeleteAsync($"/api/methodology/drafts/{baselineId}")).StatusCode);
    }

    // =============================================================================================================
    // Editing
    // =============================================================================================================

    [Fact]
    public async Task Editing_a_point_value_changes_proposed_scores_but_not_live_scores_until_published()
    {
        var before = await LiveAsync();
        var (id, rules) = await NewDraftAsync("Exit worth 30");
        rules = Edit(rules, "exit", c => c with { MaxPoints = 30 });
        await SaveAsync(id, rules);

        var preview = await PreviewAsync(id);
        Assert.True(preview.GetProperty("scoresChanged").GetInt32() > 0);
        Assert.Equal(TestWebAppFactory.SeededSiteCount, preview.GetProperty("sites").GetInt32());
        Assert.Equal(before, await LiveAsync());                    // preview never touches live scores

        await PublishOkAsync(id, preview);
        var after = await LiveAsync();
        await AssertLiveMatchesEngineAsync(rules);
        // A site at the exit (≤ 0.5 mi) gains exactly 10 points.
        var near = await NearExitSiteAsync();
        Assert.Equal(Math.Round(before[near].Score + 10, 1), after[near].Score);
    }

    [Fact]
    public async Task Editing_thresholds_and_score_bands()
    {
        var (id, rules) = await NewDraftAsync("Open-land bands");
        rules = Edit(rules, "openLand", c => c with
        {
            Cases =
            [
                Band("openAcresEst", ConditionOp.Gte, 4, 25),
                Band("openAcresEst", ConditionOp.Gte, 2, 12),
                Band("openAcresEst", ConditionOp.Between, 0.5, 6, 1.999),
            ],
            DefaultPoints = -20
        });
        await SaveAsync(id, rules);
        await PublishOkAsync(id, await PreviewAsync(id));
        await AssertLiveMatchesEngineAsync(rules);

        // Detail breakdown uses the new bands: a 4.5-ac open site earns 25.
        var site = await FirstSiteAsync("&minOpenAcres=4.5");
        var line = (await DetailLinesAsync(site.Id)).Single(l => l.GetProperty("key").GetString() == "openLand");
        Assert.Equal(25, line.GetProperty("points").GetDouble());
        Assert.Equal(25, line.GetProperty("max").GetDouble());
        Assert.Equal(-20, line.GetProperty("min").GetDouble());
    }

    [Fact]
    public async Task Unreachable_band_order_is_reported_as_a_warning()
    {
        var rules = Edit(BaselineMethodology.Create(), "openLand", c => c with
        {
            Cases = [Band("openAcresEst", ConditionOp.Gte, 1, 10), Band("openAcresEst", ConditionOp.Gte, 3, 20)]
        });
        var v = await Json(await Anon.PostAsJsonAsync("/api/methodology/validate", rules, J));
        Assert.True(v.GetProperty("valid").GetBoolean());
        Assert.Contains(v.GetProperty("issues").EnumerateArray(), i => i.GetProperty("message").GetString()!.Contains("can never match"));
    }

    [Fact]
    public async Task Adding_a_criterion_using_an_existing_field()
    {
        var (id, rules) = await NewDraftAsync("Warehouse bonus");
        rules = rules with
        {
            Criteria = [.. rules.Criteria, new Criterion
            {
                Key = "manyWarehouses", Name = "Many large warehouses", Category = CriterionCategory.Bonus, Kind = CriterionKind.Cases,
                Explanation = "Bonus for 5+ large warehouses within 2 mi.",
                Cases = [Band("largeWarehouses2Mi", ConditionOp.Gte, 5, 7)]
            }]
        };
        await SaveAsync(id, rules);
        var preview = await PreviewAsync(id);
        var impact = preview.GetProperty("criteria").EnumerateArray().Single(c => c.GetProperty("key").GetString() == "manyWarehouses");
        Assert.True(impact.GetProperty("sitesNonZero").GetInt32() > 0);
        Assert.Contains(preview.GetProperty("changes").EnumerateArray(), c => string.Equals(c.GetProperty("change").GetString(), "Added", StringComparison.OrdinalIgnoreCase));

        await PublishOkAsync(id, preview);
        await AssertLiveMatchesEngineAsync(rules);
        var site = await FirstSiteAsync("");
        var detail = await Json(await Anon.GetAsync($"/api/sites/{site.Id}"));
        var lines = detail.GetProperty("scoreLines").EnumerateArray().ToList();
        Assert.Contains(lines, l => l.GetProperty("key").GetString() == "manyWarehouses" && l.GetProperty("group").GetString() == "Bonus");
        // Breakdown always adds up to the displayed score.
        Assert.Equal(detail.GetProperty("summary").GetProperty("score").GetDouble(),
            Math.Round(lines.Sum(l => l.GetProperty("points").GetDouble()), 1));
    }

    [Fact]
    public async Task Adding_a_criterion_with_unavailable_data_is_rejected_or_uses_the_explicit_missing_policy()
    {
        var (id, rules) = await NewDraftAsync("Unavailable data");

        // (a) a field that does not exist in the data → 400, nothing saved
        var bogus = rules with { Criteria = [.. rules.Criteria, new Criterion
        {
            Key = "zoningScore", Name = "Zoning", Category = CriterionCategory.Site, Kind = CriterionKind.Percentile, Field = "zoningDensity", MaxPoints = 5
        }] };
        var bad = await Admin.PutAsJsonAsync($"/api/methodology/drafts/{id}", new { name = "x", rules = bogus }, J);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Contains("zoningDensity", await bad.Content.ReadAsStringAsync());

        // (b) a real field with gaps (contamination: not checked for any seeded site) → policy applied, counted, never imputed
        var fields = await Json(await Anon.GetAsync("/api/methodology/fields"));
        var contam = fields.EnumerateArray().Single(x => x.GetProperty("key").GetString() == "knownContaminatedSites");
        Assert.Equal(TestWebAppFactory.SeededSiteCount, contam.GetProperty("missing").GetInt32());

        var withGaps = rules with { Criteria = [.. rules.Criteria, new Criterion
        {
            Key = "contamination", Name = "Known contamination", Category = CriterionCategory.Penalty, Kind = CriterionKind.Cases,
            Cases = [Band("knownContaminatedSites", ConditionOp.Gt, 0, -8)],
            Missing = new MissingDataPolicy { Mode = MissingDataMode.FixedPoints, Points = -2 }
        }] };
        var saved = await SaveAsync(id, withGaps);
        Assert.Contains(saved.GetProperty("issues").EnumerateArray(), i => i.GetProperty("message").GetString()!.Contains("missing for some sites"));
        var view = saved.GetProperty("criteria").EnumerateArray().Single(c => c.GetProperty("key").GetString() == "contamination");
        Assert.True(view.GetProperty("usesMissableData").GetBoolean());
        Assert.Contains("−2", view.GetProperty("missingPolicy").GetString());

        var before = await LiveAsync();
        var preview = await PreviewAsync(id);
        var impact = preview.GetProperty("criteria").EnumerateArray().Single(c => c.GetProperty("key").GetString() == "contamination");
        Assert.Equal(TestWebAppFactory.SeededSiteCount, impact.GetProperty("sitesMissingData").GetInt32());
        await PublishOkAsync(id, preview);
        var after = await LiveAsync();
        Assert.All(after, kv => Assert.Equal(Math.Round(before[kv.Key].Score - 2, 1), kv.Value.Score));

        var site = await FirstSiteAsync("");
        var line = (await DetailLinesAsync(site.Id)).Single(l => l.GetProperty("key").GetString() == "contamination");
        Assert.Contains("no data", line.GetProperty("basis").GetString());
    }

    [Fact]
    public async Task Disabling_and_re_enabling_a_rule()
    {
        var baseline = await LiveAsync();
        var (id, rules) = await NewDraftAsync("No flood penalty");
        var disabled = Edit(rules, "flood", c => c with { Enabled = false });
        await SaveAsync(id, disabled);
        var preview = await PreviewAsync(id);
        Assert.Contains(preview.GetProperty("changes").EnumerateArray(), c => string.Equals(c.GetProperty("change").GetString(), "Disabled", StringComparison.OrdinalIgnoreCase));
        await PublishOkAsync(id, preview);
        await AssertLiveMatchesEngineAsync(disabled);
        var site = await FirstSiteAsync("");
        Assert.DoesNotContain(await DetailLinesAsync(site.Id), l => l.GetProperty("key").GetString() == "flood");

        // Re-enable in a new draft: identical rules to the baseline → identical scores and ranks.
        var (id2, rules2) = await NewDraftAsync("Flood back on");
        var enabled = Edit(rules2, "flood", c => c with { Enabled = true });
        var saved = await SaveAsync(id2, enabled);
        Assert.True(saved.GetProperty("version").GetProperty("identicalToBaseline").GetBoolean());
        await PublishOkAsync(id2, await PreviewAsync(id2));
        Assert.Equal(baseline, await LiveAsync());
    }

    [Fact]
    public async Task Positive_and_negative_adjustments()
    {
        var before = await LiveAsync();
        var (id, rules) = await NewDraftAsync("Adjustments");
        rules = rules with { Criteria = [.. rules.Criteria,
            new Criterion { Key = "freightBonus", Name = "Freight bonus", Category = CriterionCategory.Bonus, Kind = CriterionKind.Cases,
                Cases = [new ScoreCase { Points = 6, Conditions = [new Condition { Field = "freightUse", Op = ConditionOp.IsTrue }] }] },
            new Criterion { Key = "commercialPenalty", Name = "Commercial-only penalty", Category = CriterionCategory.Penalty, Kind = CriterionKind.Cases,
                Cases = [new ScoreCase { Points = -4, Conditions = [new Condition { Field = "propClass", Op = ConditionOp.AnyLotIn, Values = ["4A"] }] }] }] };
        await SaveAsync(id, rules);
        await PublishOkAsync(id, await PreviewAsync(id));
        await AssertLiveMatchesEngineAsync(rules);

        var list = await AllSitesAsync();
        var freight = list.First(s => s.GetProperty("freight").GetBoolean() && s.GetProperty("propClass").GetString() == "4B");
        var commercial = list.First(s => !s.GetProperty("freight").GetBoolean() && s.GetProperty("propClass").GetString() == "4A");
        var after = await LiveAsync();
        Assert.Equal(Math.Round(before[Sid(freight)].Score + 6, 1), after[Sid(freight)].Score);
        Assert.Equal(Math.Round(before[Sid(commercial)].Score - 4, 1), after[Sid(commercial)].Score);
    }

    [Fact]
    public async Task Ties_rank_stably_by_distance_then_workbook_rank_then_site_id()
    {
        var (id, rules) = await NewDraftAsync("Everything ties");
        // A single criterion every seeded site scores 0 on → all scores tie within each segment.
        rules = new MethodologyRules { Criteria = [new Criterion
        {
            Key = "onlyPort", Name = "Port only", Category = CriterionCategory.TruckDemand, Kind = CriterionKind.Cases,
            Cases = [Band("milesToPort", ConditionOp.Lt, -1, 5)]
        }] };
        await SaveAsync(id, rules);
        await PreviewAsync(id);
        var first = await ComparisonAsync(id);
        await PreviewAsync(id);                                       // recompute: identical ranks
        Assert.Equal(first, await ComparisonAsync(id));

        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sites = await db.CandidateSites.AsNoTracking().ToListAsync();
        foreach (var seg in sites.GroupBy(s => s.Segment))
        {
            var expected = seg.OrderBy(s => s.MilesToExit).ThenBy(s => s.RankInSegment).ThenBy(s => s.SiteId, StringComparer.Ordinal)
                .Select((s, i) => (s.SiteId, Rank: i + 1));
            foreach (var (siteId, rank) in expected) Assert.Equal(rank, first[siteId]);
        }
    }

    // =============================================================================================================
    // Publish / restore / history / persistence
    // =============================================================================================================

    [Fact]
    public async Task Publish_then_restore_original_methodology_through_preview_and_atomic_publish()
    {
        var original = await LiveAsync();
        var baselineId = await BaselineIdAsync();
        var (id, rules) = await NewDraftAsync("Demand-heavy");
        rules = Edit(rules, "fleets", c => c with { MaxPoints = 24 });
        await SaveAsync(id, rules);
        await PublishOkAsync(id, await PreviewAsync(id));
        Assert.NotEqual(original, await LiveAsync());
        Assert.Equal(id, (await ActiveAsync()).GetProperty("version").GetProperty("id").GetInt32());
        Assert.Equal(baselineId, (await ActiveAsync()).GetProperty("previousActiveVersionId").GetInt32());

        // Restore: the baseline goes through the same preview + confirmed atomic publish.
        var preview = await PreviewAsync(baselineId);
        Assert.True(preview.GetProperty("scoresChanged").GetInt32() > 0);
        await PublishOkAsync(baselineId, preview);
        Assert.Equal(original, await LiveAsync());

        var versions = await Json(await Anon.GetAsync("/api/methodology/versions"));
        var published = versions.EnumerateArray().Single(v => v.GetProperty("id").GetInt32() == id);
        Assert.Equal("Published", published.GetProperty("status").GetString());
        var acts = await Json(await Anon.GetAsync("/api/methodology/activations"));
        Assert.Equal("Restore", acts[0].GetProperty("reason").GetString());
        Assert.Equal(baselineId, acts[0].GetProperty("versionId").GetInt32());
        Assert.Equal("Publish", acts[1].GetProperty("reason").GetString());

        // Published rules are immutable; they can be duplicated into a new draft.
        Assert.Equal(HttpStatusCode.Conflict, (await Admin.PutAsJsonAsync($"/api/methodology/drafts/{id}", new { name = "x", rules }, J)).StatusCode);
        var dup = await Json(await Admin.PostAsJsonAsync($"/api/methodology/versions/{id}/duplicate", new { name = "copy" }));
        Assert.Equal("Draft", dup.GetProperty("version").GetProperty("status").GetString());
    }

    [Fact]
    public async Task Publish_requires_confirmation_a_current_preview_and_the_expected_active_version()
    {
        var active = await ActiveIdAsync();
        var (id, rules) = await NewDraftAsync("Guarded");
        await SaveAsync(id, Edit(rules, "port", c => c with { DefaultPoints = 1 }));

        // Not previewed yet → stale.
        Assert.Equal(HttpStatusCode.Conflict, (await PublishAsync(id, active, "whatever")).StatusCode);
        var preview = await PreviewAsync(id);
        var hash = preview.GetProperty("rulesHash").GetString()!;
        // No confirmation → 400.
        Assert.Equal(HttpStatusCode.BadRequest, (await PublishAsync(id, active, hash, confirm: false)).StatusCode);
        // Wrong expected active version → 409.
        Assert.Equal(HttpStatusCode.Conflict, (await PublishAsync(id, active + 999, hash)).StatusCode);
        // Edited after preview → 409 until previewed again.
        await SaveAsync(id, Edit(rules, "port", c => c with { DefaultPoints = 2 }));
        Assert.Equal(HttpStatusCode.Conflict, (await PublishAsync(id, active, hash)).StatusCode);
        Assert.Equal(active, await ActiveIdAsync());
    }

    [Fact]
    public async Task Failed_recalculation_rolls_back_and_leaves_the_active_version_unchanged()
    {
        var before = await LiveAsync();
        var active = await ActiveIdAsync();
        var (id, rules) = await NewDraftAsync("Will fail");
        rules = Edit(rules, "exit", c => c with { MaxPoints = 40 });
        await SaveAsync(id, rules);
        var preview = await PreviewAsync(id);

        f.Faults.FailNextActivation = true;   // throws AFTER all 1,500 live rows were updated, before commit
        var resp = await PublishAsync(id, active, preview.GetProperty("rulesHash").GetString()!);
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
        Assert.Contains("rolled back", await resp.Content.ReadAsStringAsync());
        Assert.Equal(active, await ActiveIdAsync());
        Assert.Equal(before, await LiveAsync());
        Assert.Equal("Draft", (await Json(await Anon.GetAsync($"/api/methodology/versions/{id}"))).GetProperty("version").GetProperty("status").GetString());

        // The same preview can then be published normally.
        await PublishOkAsync(id, preview);
        await AssertLiveMatchesEngineAsync(rules);
    }

    [Fact]
    public async Task Concurrent_conflicting_publications_cannot_both_succeed()
    {
        var active = await ActiveIdAsync();
        var (a, ra) = await NewDraftAsync("A");
        var (b, rb) = await NewDraftAsync("B");
        await SaveAsync(a, Edit(ra, "exit", c => c with { MaxPoints = 25 }));
        await SaveAsync(b, Edit(rb, "exit", c => c with { MaxPoints = 15 }));
        var pa = (await PreviewAsync(a)).GetProperty("rulesHash").GetString()!;
        var pb = (await PreviewAsync(b)).GetProperty("rulesHash").GetString()!;

        var results = await Task.WhenAll(PublishAsync(a, active, pa), PublishAsync(b, active, pb));
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict);
        var winner = results[0].StatusCode == HttpStatusCode.OK ? (a, ra: Edit(ra, "exit", c => c with { MaxPoints = 25 }))
                                                                : (b, ra: Edit(rb, "exit", c => c with { MaxPoints = 15 }));
        Assert.Equal(winner.Item1, await ActiveIdAsync());
        await AssertLiveMatchesEngineAsync(winner.ra);
    }

    [Fact]
    public async Task Active_methodology_persists_after_api_restart()
    {
        var (id, rules) = await NewDraftAsync("Survives restart");
        await SaveAsync(id, Edit(rules, "noFastCharger", c => c with { Cases = [Band("milesToAnyDcfc", ConditionOp.Gt, 2, 9)] }));
        await PublishOkAsync(id, await PreviewAsync(id));
        var live = await LiveAsync();

        await using var restarted = new MethodologyWebAppFactory();   // new host, same database
        var client = restarted.CreateClient();
        var active = await Json(await client.GetAsync("/api/methodology/active"));
        Assert.Equal(id, active.GetProperty("version").GetProperty("id").GetInt32());
        var resp = await client.GetAsync("/api/sites?page=1&pageSize=5");
        Assert.Equal(id.ToString(), resp.Headers.GetValues("X-Methodology-Version").Single());
        Assert.Equal(live, await LiveAsync(client));
    }

    [Fact]
    public async Task Simulated_reimport_keeps_scores_bookmarks_and_screening_and_rescore_reapplies_the_active_version()
    {
        var (id, rules) = await NewDraftAsync("Before re-import");
        rules = Edit(rules, "traffic", c => c with { MaxPoints = 20 });
        await SaveAsync(id, rules);
        await PublishOkAsync(id, await PreviewAsync(id));
        var live = await LiveAsync();
        var site = f.SampleSiteId;
        Assert.True((await Anon.PostAsJsonAsync("/api/saved-sites", new { siteId = site })).IsSuccessStatusCode);

        await f.SimulateReimportAsync();     // every CandidateSite row deleted and re-inserted (new identity ids)
        var r = await Admin.PostAsync("/api/admin/rescore", null);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(id, (await Json(r)).GetProperty("methodologyVersionId").GetInt32());
        Assert.Equal(live, await LiveAsync());
        await AssertLiveMatchesEngineAsync(rules);
        var saved = await Json(await Anon.GetAsync("/api/saved-sites"));
        Assert.Contains(saved.EnumerateArray(), s => s.GetProperty("siteId").GetString() == site);
    }

    // =============================================================================================================
    // Every view shows the active version
    // =============================================================================================================

    [Fact]
    public async Task All_dashboard_views_and_exports_show_the_newly_published_scores()
    {
        var (id, rules) = await NewDraftAsync("Consistency");
        rules = Edit(rules, "propertyClass", c => c with { DefaultPoints = 0 });
        rules = Edit(rules, "warehouses", c => c with { MaxPoints = 14 });
        await SaveAsync(id, rules);
        await PublishOkAsync(id, await PreviewAsync(id));
        await AssertLiveMatchesEngineAsync(rules);

        var top = (await Json(await Anon.GetAsync("/api/sites?page=1&pageSize=3"))).GetProperty("items").EnumerateArray().ToList();
        var siteIds = top.Select(Sid).ToList();
        var map = (await Json(await Anon.GetAsync("/api/sites/map"))).EnumerateArray().ToDictionary(p => p.GetProperty("id").GetInt32());
        Assert.True((await Anon.PostAsJsonAsync("/api/saved-sites", new { siteId = siteIds[0] })).IsSuccessStatusCode);
        var saved = (await Json(await Anon.GetAsync("/api/saved-sites"))).EnumerateArray().Single(s => s.GetProperty("siteId").GetString() == siteIds[0]);
        var compare = await Json(await Anon.GetAsync($"/api/compare?siteIds={string.Join(",", siteIds)}"));
        var csv = (await (await Anon.GetAsync("/api/sites/export")).Content.ReadAsStringAsync()).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var research = (await (await Anon.GetAsync("/api/sites/export?format=research")).Content.ReadAsStringAsync()).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var dash = await Json(await Anon.GetAsync("/api/dashboard/summary"));
        var listResp = await Anon.GetAsync("/api/sites?page=1&pageSize=1");
        Assert.Equal(id.ToString(), listResp.Headers.GetValues("X-Methodology-Version").Single());

        var expectedMax = (await LiveAsync()).Values.Max(v => v.Score);
        Assert.Equal(expectedMax, dash.GetProperty("kpis").GetProperty("topScore").GetDouble());

        for (var i = 0; i < top.Count; i++)
        {
            var item = top[i];
            var score = item.GetProperty("score").GetDouble();
            var rank = item.GetProperty("rank").GetInt32();
            var detail = await Json(await Anon.GetAsync($"/api/sites/{item.GetProperty("id").GetInt32()}"));
            Assert.Equal(score, detail.GetProperty("summary").GetProperty("score").GetDouble());
            Assert.Equal(id, detail.GetProperty("methodologyVersionId").GetInt32());
            Assert.Equal(score, Math.Round(detail.GetProperty("scoreLines").EnumerateArray().Sum(l => l.GetProperty("points").GetDouble()), 1));
            Assert.Equal(score, map[item.GetProperty("id").GetInt32()].GetProperty("score").GetDouble());
            Assert.Equal(rank, map[item.GetProperty("id").GetInt32()].GetProperty("rank").GetInt32());
            var csvRow = csv.Single(l => l.Contains("," + siteIds[i] + ","));
            Assert.StartsWith($"{item.GetProperty("segment").GetInt32()},{rank},{score.ToString(System.Globalization.CultureInfo.InvariantCulture)},", csvRow);
            Assert.Contains($",v{id} Consistency", csvRow);
            Assert.Contains($",v{id} Consistency", research.Single(l => l.StartsWith(siteIds[i] + ",")));
            var cmp = compare.GetProperty("sites").EnumerateArray().Single(s => s.GetProperty("siteId").GetString() == siteIds[i]);
            Assert.Equal(score, cmp.GetProperty("score").GetDouble());
            Assert.Equal(rank, cmp.GetProperty("segmentRank").GetInt32());
        }
        Assert.Equal(top[0].GetProperty("score").GetDouble(), saved.GetProperty("score").GetDouble());
        Assert.Equal(top[0].GetProperty("rank").GetInt32(), saved.GetProperty("rank").GetInt32());

        // Screening queue / top-N uses the same ranks.
        var top1 = (await Json(await Anon.GetAsync("/api/sites?topN=1&pageSize=50"))).GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(6, top1.Count);
        Assert.All(top1, s => Assert.Equal(1, s.GetProperty("rank").GetInt32()));
    }

    // =============================================================================================================
    // Helpers
    // =============================================================================================================

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        var body = await r.Content.ReadAsStringAsync();
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode} {r.RequestMessage?.RequestUri}: {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static string Sid(JsonElement e) => e.GetProperty("siteId").GetString()!;

    private static ScoreCase Band(string field, ConditionOp op, double value, double points, double? value2 = null) =>
        new() { Points = points, Conditions = [new Condition { Field = field, Op = op, Value = value, Value2 = value2 }] };

    private static MethodologyRules Edit(MethodologyRules r, string key, Func<Criterion, Criterion> edit) =>
        r with { Criteria = r.Criteria.Select(c => c.Key == key ? edit(c) : c).ToList() };

    private async Task<JsonElement> ActiveAsync() => await Json(await Anon.GetAsync("/api/methodology/active"));
    private async Task<int> ActiveIdAsync() => (await ActiveAsync()).GetProperty("version").GetProperty("id").GetInt32();

    private async Task<int> BaselineIdAsync() =>
        (await Json(await Anon.GetAsync("/api/methodology/versions"))).EnumerateArray()
        .Single(v => v.GetProperty("isBaseline").GetBoolean()).GetProperty("id").GetInt32();

    private async Task<(int Id, MethodologyRules Rules)> NewDraftAsync(string name)
    {
        var d = await Json(await Admin.PostAsJsonAsync("/api/methodology/drafts", new { name }));
        return (d.GetProperty("version").GetProperty("id").GetInt32(),
            JsonSerializer.Deserialize<MethodologyRules>(d.GetProperty("rules").GetRawText(), J)!);
    }

    private async Task<JsonElement> SaveAsync(int id, MethodologyRules rules)
    {
        var name = (await Json(await Anon.GetAsync($"/api/methodology/versions/{id}"))).GetProperty("version").GetProperty("name").GetString();
        return await Json(await Admin.PutAsJsonAsync($"/api/methodology/drafts/{id}", new { name, changeSummary = "test", rules }, J));
    }

    private async Task<JsonElement> PreviewAsync(int id) => await Json(await Admin.PostAsync($"/api/methodology/versions/{id}/preview?topN=5", null));

    private Task<HttpResponseMessage> PublishAsync(int id, int expectedActive, string hash, bool confirm = true) =>
        Admin.PostAsJsonAsync($"/api/methodology/versions/{id}/publish",
            new { confirm, expectedActiveVersionId = expectedActive, rulesHash = hash, note = "test" });

    private async Task PublishOkAsync(int id, JsonElement preview) =>
        await Json(await PublishAsync(id, preview.GetProperty("comparedToVersionId").GetInt32(), preview.GetProperty("rulesHash").GetString()!));

    private async Task RestoreBaselineAsync()
    {
        var baselineId = await BaselineIdAsync();
        if (await ActiveIdAsync() == baselineId) return;
        await PublishOkAsync(baselineId, await PreviewAsync(baselineId));
    }

    /// <summary>SiteId → (score, rank) from the paged ranked list (what the dashboard table shows).</summary>
    private Task<Dictionary<string, (double Score, int Rank)>> LiveAsync() => LiveAsync(Anon);

    private static async Task<Dictionary<string, (double Score, int Rank)>> LiveAsync(HttpClient client) =>
        (await AllSitesAsync(client)).ToDictionary(Sid, s => (s.GetProperty("score").GetDouble(), s.GetProperty("rank").GetInt32()));

    private Task<List<JsonElement>> AllSitesAsync() => AllSitesAsync(Anon);

    private static async Task<List<JsonElement>> AllSitesAsync(HttpClient client)
    {
        var all = new List<JsonElement>();
        for (var page = 1; ; page++)
        {
            var items = (await Json(await client.GetAsync($"/api/sites?page={page}&pageSize=500"))).GetProperty("items").EnumerateArray().ToList();
            all.AddRange(items);
            if (items.Count < 500) return all;
        }
    }

    private async Task<(int Id, string SiteId)> FirstSiteAsync(string query)
    {
        var item = (await Json(await Anon.GetAsync($"/api/sites?page=1&pageSize=1{query}"))).GetProperty("items")[0];
        return (item.GetProperty("id").GetInt32(), Sid(item));
    }

    private async Task<string> NearExitSiteAsync() =>
        Sid((await AllSitesAsync()).First(s => s.GetProperty("milesToExit").GetDouble() <= 0.5));

    private async Task<List<JsonElement>> DetailLinesAsync(int id) =>
        (await Json(await Anon.GetAsync($"/api/sites/{id}"))).GetProperty("scoreLines").EnumerateArray().ToList();

    private async Task<Dictionary<string, int>> ComparisonAsync(int id)
    {
        var map = new Dictionary<string, int>();
        for (var page = 1; ; page++)
        {
            var rows = (await Json(await Anon.GetAsync($"/api/methodology/versions/{id}/comparison?page={page}&pageSize=200"))).GetProperty("rows").EnumerateArray().ToList();
            foreach (var r in rows) map[Sid(r)] = r.GetProperty("proposedRank").GetInt32();
            if (rows.Count < 200) return map;
        }
    }

    /// <summary>Independent check: the engine run in-process over the DB's sites equals what every view serves.</summary>
    private async Task AssertLiveMatchesEngineAsync(MethodologyRules rules)
    {
        using var scope = f.Services.CreateScope();
        var sites = await scope.ServiceProvider.GetRequiredService<IMethodologyStore>().LoadSitesForScoringAsync();
        var expected = MethodologyEngine.Evaluate(sites, rules).Sites.ToDictionary(r => r.SiteId, r => (r.Score, r.Rank));
        var live = await LiveAsync();
        Assert.Equal(expected.Count, live.Count);
        Assert.All(expected, kv => Assert.Equal(kv.Value, live[kv.Key]));
    }
}
