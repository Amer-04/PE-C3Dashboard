using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace NJTruckCharging.Tests.Integration;

/// <summary>Site Screening workflow (same class/fixture as the other integration tests so DB resets don't race).</summary>
public partial class ApiIntegrationTests
{
    private static async Task<HttpResponseMessage> Screen(HttpClient c, string siteId, string decision, string? evidence) =>
        await c.PutAsJsonAsync("/api/screening", new { siteId, decision, evidence });

    private async Task<int> CandidateIdOf(HttpClient c, string siteId)
    {
        var page = await Json(await c.GetAsync($"/api/sites?search={siteId}&page=1&pageSize=5"));
        return page.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("siteId").GetString() == siteId).GetProperty("id").GetInt32();
    }

    private static async Task<int> TotalFor(HttpClient c, string query) =>
        (await Json(await c.GetAsync($"/api/sites?{query}&page=1&pageSize=1"))).GetProperty("totalCount").GetInt32();

    [Fact]
    public async Task Screening_defaults_to_not_reviewed()
    {
        var dto = await Json(await Client().GetAsync("/api/screening?siteId=TEST-0010"));
        Assert.Equal("NotReviewed", dto.GetProperty("decision").GetString());
        Assert.Equal(JsonValueKind.Null, dto.GetProperty("evidence").ValueKind);
        Assert.Equal(0, dto.GetProperty("history").GetArrayLength());
    }

    [Theory]
    [InlineData("Promising")]
    [InlineData("NeedsMoreResearch")]
    [InlineData("Unsuitable")]
    public async Task Screening_decisions_require_evidence(string decision)
    {
        var client = Client();
        Assert.Equal(HttpStatusCode.BadRequest, (await Screen(client, "TEST-0010", decision, null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Screen(client, "TEST-0010", decision, "   ")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Screen(client, "TEST-0010", decision, new string('x', 4001))).StatusCode);
        // Nothing was stored.
        var dto = await Json(await client.GetAsync("/api/screening?siteId=TEST-0010"));
        Assert.Equal("NotReviewed", dto.GetProperty("decision").GetString());
    }

    [Fact]
    public async Task Screening_rejects_unknown_sites_and_decisions()
    {
        var client = Client();
        Assert.Equal(HttpStatusCode.NotFound, (await Screen(client, "NO-SUCH-SITE", "Promising", "aerial")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Screen(client, "TEST-0010", "Approved", "aerial")).StatusCode);
    }

    [Fact]
    public async Task Screening_status_changes_persist_evidence_and_history()
    {
        var client = Client();
        const string siteId = "TEST-0010";

        Assert.Equal(HttpStatusCode.OK, (await Screen(client, siteId, "Promising", "Satellite: ~3 ac paved yard, 2 curb cuts on frontage road.")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Screen(client, siteId, "NeedsMoreResearch", "Street View 2024 shows a gate; need to confirm trailer access.")).StatusCode);
        // Same decision + same evidence → no duplicate history entry.
        await Screen(client, siteId, "NeedsMoreResearch", "Street View 2024 shows a gate; need to confirm trailer access.");
        Assert.Equal(HttpStatusCode.OK, (await Screen(client, siteId, "Unsuitable", "  Yard is fully built out with a warehouse addition (2025 imagery).  ")).StatusCode);

        var dto = await Json(await client.GetAsync($"/api/screening?siteId={siteId}"));
        Assert.Equal("Unsuitable", dto.GetProperty("decision").GetString());
        Assert.Equal("Yard is fully built out with a warehouse addition (2025 imagery).", dto.GetProperty("evidence").GetString()); // trimmed
        var history = dto.GetProperty("history").EnumerateArray().Select(h => h.GetProperty("decision").GetString()).ToArray();
        Assert.Equal(new[] { "Unsuitable", "NeedsMoreResearch", "Promising" }, history); // newest first, no duplicate
        Assert.Contains("curb cuts", dto.GetProperty("history")[2].GetProperty("evidence").GetString());

        // Surfaced on the list row and the site detail.
        var id = await CandidateIdOf(client, siteId);
        var detail = await Json(await client.GetAsync($"/api/sites/{id}"));
        Assert.Equal("Unsuitable", detail.GetProperty("summary").GetProperty("screeningDecision").GetString());
        Assert.Contains("warehouse addition", detail.GetProperty("screeningEvidence").GetString());
        Assert.StartsWith("https://www.google.com/maps/@?api=1&map_action=map&center=", detail.GetProperty("satelliteUrl").GetString());
        Assert.Contains("basemap=satellite", detail.GetProperty("satelliteUrl").GetString());
        Assert.Contains("map_action=pano", detail.GetProperty("streetViewUrl").GetString());

        // Reset to Not reviewed (evidence optional) — history keeps the trail.
        Assert.Equal(HttpStatusCode.OK, (await Screen(client, siteId, "NotReviewed", null)).StatusCode);
        var reset = await Json(await client.GetAsync($"/api/screening?siteId={siteId}"));
        Assert.Equal("NotReviewed", reset.GetProperty("decision").GetString());
        Assert.Equal(4, reset.GetProperty("history").GetArrayLength());
    }

    [Fact]
    public async Task Screening_filter_list_kpis_summary_and_csv_agree()
    {
        var client = Client();
        await Screen(client, "TEST-0010", "Promising", "Paved yard visible");
        await Screen(client, "TEST-0011", "Promising", "Open gravel lot");
        await Screen(client, "TEST-0012", "NeedsMoreResearch", "Access unclear");
        await Screen(client, "TEST-0013", "Unsuitable", "Wetlands on aerial");

        var all = await TotalFor(client, "sortBy=Score");
        Assert.Equal(2, await TotalFor(client, "screeningDecisions=promising"));
        Assert.Equal(1, await TotalFor(client, "screeningDecisions=research"));
        Assert.Equal(1, await TotalFor(client, "screeningDecisions=unsuitable"));
        Assert.Equal(all - 4, await TotalFor(client, "screeningDecisions=notreviewed"));
        Assert.Equal(3, await TotalFor(client, "screeningDecisions=promising&screeningDecisions=research"));
        // Combined with other filters.
        Assert.Equal(0, await TotalFor(client, "screeningDecisions=promising&search=TEST-0013"));

        var rows = (await Json(await client.GetAsync("/api/sites?screeningDecisions=promising&page=1&pageSize=10"))).GetProperty("items");
        Assert.All(rows.EnumerateArray(), r => Assert.Equal("Promising", r.GetProperty("screeningDecision").GetString()));

        var kpis = await Json(await client.GetAsync("/api/dashboard/summary?screeningDecisions=promising"));
        Assert.Equal(2, kpis.GetProperty("kpis").GetProperty("sites").GetInt32());

        var summary = await Json(await client.GetAsync("/api/screening/summary"));
        Assert.Equal(2, summary.GetProperty("promising").GetInt32());
        Assert.Equal(4, summary.GetProperty("reviewed").GetInt32());
        Assert.Equal(1, summary.GetProperty("needsMoreResearch").GetInt32());
        Assert.Equal(1, summary.GetProperty("unsuitable").GetInt32());
        Assert.Equal(all - 4, summary.GetProperty("notReviewed").GetInt32());

        var csv = await (await client.GetAsync("/api/sites/export?screeningDecisions=unsuitable")).Content.ReadAsStringAsync();
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToList();
        Assert.EndsWith(",screening_decision,screening_evidence", lines[0]);
        Assert.Equal(2, lines.Count);
        Assert.EndsWith(",Unsuitable,Wetlands on aerial", lines[1]);
    }

    [Fact]
    public async Task Screening_progress_is_reported_per_segment_and_for_top_ranks()
    {
        var client = Client();
        // A site that is rank #1 in its segment, plus two ordinary sites in segments 1 and 2.
        var top = (await Json(await client.GetAsync("/api/sites?segments=4&topN=1&page=1&pageSize=1"))).GetProperty("items")[0];
        var topSiteId = top.GetProperty("siteId").GetString()!;
        await Screen(client, topSiteId, "Promising", "Top-ranked yard looks open");
        await Screen(client, "TEST-0001", "Promising", "Paved yard");
        await Screen(client, "TEST-0002", "Unsuitable", "Residential access only");

        var all = await Json(await client.GetAsync("/api/screening/summary"));
        var segs = all.GetProperty("segments").EnumerateArray().ToDictionary(s => s.GetProperty("segment").GetInt32());
        Assert.Equal(6, segs.Count);
        Assert.Equal(await TotalFor(client, "sortBy=Score"), segs.Values.Sum(s => s.GetProperty("total").GetInt32()));
        Assert.Equal(3, all.GetProperty("reviewed").GetInt32());
        Assert.Equal(1, segs[2].GetProperty("unsuitable").GetInt32());
        Assert.Equal(await TotalFor(client, "segments=2"), segs[2].GetProperty("total").GetInt32());
        Assert.Equal(segs[2].GetProperty("total").GetInt32() - 1, segs[2].GetProperty("notReviewed").GetInt32());
        Assert.Equal(1, segs[4].GetProperty("promising").GetInt32());

        // Top-1 scope: exactly one site per segment, and only the rank-1 decision counts.
        var top1 = await Json(await client.GetAsync("/api/screening/summary?topN=1"));
        Assert.Equal(1, top1.GetProperty("topN").GetInt32());
        Assert.Equal(6, top1.GetProperty("total").GetInt32());
        var rankOf = async (string id) => (await Json(await client.GetAsync($"/api/sites?search={id}&page=1&pageSize=5")))
            .GetProperty("items").EnumerateArray().Single(i => i.GetProperty("siteId").GetString() == id).GetProperty("rank").GetInt32();
        var expectedReviewed = 1 + (await rankOf("TEST-0001") == 1 ? 1 : 0) + (await rankOf("TEST-0002") == 1 ? 1 : 0);
        Assert.Equal(expectedReviewed, top1.GetProperty("reviewed").GetInt32());
        var seg4 = top1.GetProperty("segments").EnumerateArray().Single(s => s.GetProperty("segment").GetInt32() == 4);
        Assert.Equal(1, seg4.GetProperty("promising").GetInt32());
        Assert.Equal(0, seg4.GetProperty("notReviewed").GetInt32());
    }

    [Fact]
    public async Task Comparison_shows_screening_decisions_without_ranking()
    {
        var client = Client();
        await Screen(client, "TEST-0001", "Promising", "Large paved yard, two curb cuts");
        var cmp = await Json(await client.GetAsync("/api/compare?siteIds=TEST-0001&siteIds=TEST-0002"));

        var row = RowOf(cmp, "screening");
        Assert.Equal(new[] { "Promising", "Not reviewed" }, Strings(row.GetProperty("values")));
        Assert.Equal(new[] { "clear", "unknown" }, Strings(row.GetProperty("tones")));
        Assert.True(row.GetProperty("differs").GetBoolean());
        var ev = RowOf(cmp, "screeningEvidence");
        Assert.False(ev.GetProperty("comparable").GetBoolean());
        Assert.Equal("None recorded", Strings(ev.GetProperty("values"))[1]);

        var sites = cmp.GetProperty("sites");
        Assert.Equal("Promising", sites[0].GetProperty("screeningDecision").GetString());
        Assert.Equal(new[] { "TEST-0001", "TEST-0002" }, sites.EnumerateArray().Select(s => s.GetProperty("siteId").GetString())); // order kept, not re-ranked
        Assert.Contains("basemap=satellite", sites[0].GetProperty("satelliteUrl").GetString());

        var csv = await (await client.GetAsync("/api/compare/export?siteIds=TEST-0001&siteIds=TEST-0002")).Content.ReadAsStringAsync();
        Assert.Contains("Screening,Team screening decision,Promising,Not reviewed,yes", csv);
    }

    [Fact]
    public async Task Screening_survives_restart_and_workbook_reimport()
    {
        var client = Client();
        await Screen(client, "TEST-0020", "Promising", "Satellite: open 4 ac lot behind building");
        await Screen(client, "TEST-0021", "Unsuitable", "Street View: residential cul-de-sac access only");
        var idBefore = await CandidateIdOf(client, "TEST-0020");

        // Restart: a brand-new host over the same database.
        await using (var restarted = new TestWebAppFactory())
        {
            var c2 = restarted.CreateClient();
            var dto = await Json(await c2.GetAsync("/api/screening?siteId=TEST-0020"));
            Assert.Equal("Promising", dto.GetProperty("decision").GetString());
            Assert.Contains("open 4 ac", dto.GetProperty("evidence").GetString());
        }

        // Re-import: every candidate row is replaced and identity ids regenerate.
        await _factory.SimulateReimportAsync();
        var idAfter = await CandidateIdOf(client, "TEST-0020");
        Assert.NotEqual(idBefore, idAfter);

        var detail = await Json(await client.GetAsync($"/api/sites/{idAfter}"));
        Assert.Equal("Promising", detail.GetProperty("summary").GetProperty("screeningDecision").GetString());
        Assert.Equal(1, await TotalFor(client, "screeningDecisions=promising"));
        Assert.Equal(1, await TotalFor(client, "screeningDecisions=unsuitable"));
        var history = await Json(await client.GetAsync("/api/screening?siteId=TEST-0021"));
        Assert.Equal(1, history.GetProperty("history").GetArrayLength());
    }

    [Fact]
    public async Task Screening_never_changes_scores_or_ranks()
    {
        var client = Client();
        async Task<string> Snapshot()
        {
            var page = await Json(await client.GetAsync("/api/sites?page=1&pageSize=500&sortBy=Score&descending=true"));
            return string.Join("|", page.GetProperty("items").EnumerateArray().Select(i =>
                $"{i.GetProperty("siteId").GetString()}:{i.GetProperty("score").GetDouble()}:{i.GetProperty("rank").GetInt32()}"));
        }
        var before = await Snapshot();
        await Screen(client, "TEST-0001", "Unsuitable", "Flooded on 2023 imagery");
        await Screen(client, "TEST-0002", "Promising", "Clean yard");
        Assert.Equal(before, await Snapshot());
    }
}
