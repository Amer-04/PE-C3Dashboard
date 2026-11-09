using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace NJTruckCharging.Tests.Integration;

/// <summary>Site Comparison API (same class/fixture as the other integration tests, so DB resets don't race).</summary>
public partial class ApiIntegrationTests
{
    // Seeded rows (see TestWebAppFactory.BuildSites): TEST-0001 seg 1, TEST-0002 seg 2, TEST-0003 seg 3, TEST-0007 seg 1.

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return await r.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static JsonElement RowOf(JsonElement cmp, string key) =>
        cmp.GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("key").GetString() == key);

    private static string[] Strings(JsonElement arr) => arr.EnumerateArray().Select(v => v.GetString()!).ToArray();

    [Theory]
    [InlineData("siteIds=TEST-0001")]
    [InlineData("siteIds=TEST-0001&siteIds=TEST-0001")]
    [InlineData("siteIds=TEST-0001&siteIds=TEST-0002&siteIds=TEST-0003&siteIds=TEST-0004&siteIds=TEST-0005&siteIds=TEST-0006")]
    public async Task Compare_requires_two_to_five_distinct_sites(string query)
    {
        var client = Client();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/compare?{query}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/compare/export?{query}")).StatusCode);
    }

    [Fact]
    public async Task Compare_never_treats_unreviewed_or_unscreened_values_as_verified()
    {
        var cmp = await Json(await Client().GetAsync("/api/compare?siteIds=TEST-0001&siteIds=TEST-0002&siteIds=TEST-0003"));
        var sites = cmp.GetProperty("sites").EnumerateArray().ToList();
        Assert.Equal(new[] { "TEST-0001", "TEST-0002", "TEST-0003" }, sites.Select(s => s.GetProperty("siteId").GetString()));
        Assert.True(cmp.GetProperty("mixedSegments").GetBoolean());
        Assert.Contains("segment", cmp.GetProperty("scoreNote").GetString());

        foreach (var s in sites)
        {
            foreach (var v in new[] { "truckEntrance", "electrical", "verifiedParking", "highwayAccess", "floodEnvironmentalReview" })
                Assert.Equal("Unknown", s.GetProperty(v).GetProperty("status").GetString());
            Assert.Equal("SourceDerived", s.GetProperty("ios").GetProperty("basis").GetString());
            Assert.NotEqual("Confirmed", s.GetProperty("ios").GetProperty("classification").GetString());
            // Contamination was never screened in the seed → NotChecked (not Clear) and listed as missing.
            Assert.Equal("NotChecked", s.GetProperty("contamination").GetProperty("status").GetString());
            var missing = Strings(s.GetProperty("missingVerification"));
            Assert.Equal(FeasibilityKeyCount + 1, missing.Length);
            Assert.Contains("Contamination screening (source data)", missing);
            Assert.Contains("Outdoor-storage (IOS) use", missing);
        }

        // TEST-0001 has no open-acreage estimate → Unknown parking estimate and IOS, never a number.
        Assert.Equal(JsonValueKind.Null, sites[0].GetProperty("estimatedParkingPreliminary").ValueKind);
        Assert.Equal("unknown", RowOf(cmp, "estParking").GetProperty("tones")[0].GetString());

        foreach (var key in new[] { "truckEntrance", "electrical", "verifiedParking", "contamination", "missing" })
            Assert.All(Strings(RowOf(cmp, key).GetProperty("tones")), t => Assert.Equal("unknown", t));
        Assert.All(Strings(RowOf(cmp, "verifiedParking").GetProperty("values")), v => Assert.Equal("Not verified", v));
        Assert.All(Strings(RowOf(cmp, "truckEntrance").GetProperty("values")), v => Assert.Equal("Not reviewed", v));
        Assert.Contains("segment", RowOf(cmp, "score").GetProperty("label").GetString());

        // No winner: nothing in the payload ranks the compared sites against each other.
        var raw = cmp.GetRawText().ToLowerInvariant();
        foreach (var word in new[] { "winner", "\"best", "recommended" }) Assert.DoesNotContain(word, raw);
        // No CRM / sales fields.
        foreach (var word in new[] { "pipeline", "dealstage", "assignedto", "crm" }) Assert.DoesNotContain(word, raw);
    }

    [Fact]
    public async Task Compare_reflects_reviewer_verdicts_and_flags_differences()
    {
        var client = Client();
        await client.PutAsJsonAsync("/api/feasibility", new { siteId = "TEST-0001", itemKey = "TruckEntranceAccess", status = "Verified", sourceUrl = "https://example.org/evidence", evidenceDate = "2026-10-01", evidence = "Two 40ft curb cuts" });
        await client.PutAsJsonAsync("/api/feasibility", new { siteId = "TEST-0001", itemKey = "TruckParkingCapacity", status = "Verified", sourceUrl = "https://example.org/evidence", evidenceDate = "2026-10-01", evidence = "48 marked trailer stalls" });
        await client.PutAsJsonAsync("/api/feasibility", new { siteId = "TEST-0007", itemKey = "ElectricalService", status = "Concern", evidence = "Utility says 18-month upgrade" });

        var cmp = await Json(await client.GetAsync("/api/compare?siteIds=TEST-0007,TEST-0001")); // comma list, order kept
        var sites = cmp.GetProperty("sites").EnumerateArray().ToList();
        Assert.Equal("TEST-0007", sites[0].GetProperty("siteId").GetString());
        Assert.False(cmp.GetProperty("mixedSegments").GetBoolean()); // both segment 1

        var entrance = RowOf(cmp, "truckEntrance");
        Assert.True(entrance.GetProperty("differs").GetBoolean());
        Assert.Equal(new[] { "unknown", "verified" }, Strings(entrance.GetProperty("tones")));
        Assert.Contains("48 marked trailer stalls", Strings(RowOf(cmp, "verifiedParking").GetProperty("values"))[1]);
        Assert.Equal("concern", Strings(RowOf(cmp, "electrical").GetProperty("tones"))[0]);
        Assert.Contains("18-month upgrade", Strings(RowOf(cmp, "concerns").GetProperty("values"))[0]);

        // Same segment → the segment row does not differ; identity rows are never "compared".
        Assert.False(RowOf(cmp, "segment").GetProperty("differs").GetBoolean());
        Assert.False(RowOf(cmp, "address").GetProperty("comparable").GetBoolean());

        var missing1 = sites[1].GetProperty("missingVerification").GetArrayLength();
        var missing7 = sites[0].GetProperty("missingVerification").GetArrayLength();
        Assert.Equal(missing7 - 1, missing1); // TEST-0001 has 2 verified, TEST-0007 has 1 concern (reviewed)
        Assert.StartsWith("https://www.google.com/maps/search/?api=1&query=", sites[0].GetProperty("googleMapsUrl").GetString());
    }

    [Fact]
    public async Task Compare_reports_sites_missing_from_the_dataset()
    {
        var cmp = await Json(await Client().GetAsync("/api/compare?siteIds=TEST-0001&siteIds=NO-SUCH-SITE&siteIds=TEST-0002"));
        Assert.Equal(2, cmp.GetProperty("sites").GetArrayLength());
        Assert.Equal(new[] { "NO-SUCH-SITE" }, Strings(cmp.GetProperty("missingSiteIds")));
    }

    [Fact]
    public async Task Compare_csv_export_has_one_column_per_site_and_difference_flags()
    {
        var resp = await Client().GetAsync("/api/compare/export?siteIds=TEST-0001&siteIds=TEST-0002");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("text/csv", resp.Content.Headers.ContentType?.MediaType);
        var csv = (await resp.Content.ReadAsStringAsync()).TrimStart('﻿');
        var lines = csv.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        Assert.Equal("group,attribute,TEST-0001 (Seg 1),TEST-0002 (Seg 2),values_differ", lines[0]);
        Assert.Contains(lines, l => l.StartsWith("Highway access,Truck entrance status,Not reviewed,Not reviewed,no"));
        Assert.Contains(lines, l => l.StartsWith("Location,Geographic segment,") && l.EndsWith(",yes"));
        Assert.Contains(lines, l => l.Contains("not a head-to-head ranking"));
        Assert.Contains(lines, l => l.Contains("they are not passes"));
    }

    [Fact]
    public async Task Comparing_and_reviewing_never_changes_scores_or_ranks()
    {
        var client = Client();
        async Task<string> Snapshot()
        {
            var page = await Json(await client.GetAsync("/api/sites?page=1&pageSize=500&sortBy=Score&descending=true"));
            return string.Join("|", page.GetProperty("items").EnumerateArray().Select(i =>
                $"{i.GetProperty("siteId").GetString()}:{i.GetProperty("score").GetDouble()}:{i.GetProperty("rank").GetInt32()}"));
        }
        var before = await Snapshot();
        await client.PutAsJsonAsync("/api/feasibility", new { siteId = "TEST-0007", itemKey = "OutdoorStorageUse", status = "Verified", sourceUrl = "https://example.org/evidence", evidenceDate = "2026-10-01", evidence = "Trailer yard observed" });
        await Json(await client.GetAsync("/api/compare?siteIds=TEST-0001&siteIds=TEST-0007&siteIds=TEST-0003"));
        await client.GetAsync("/api/compare/export?siteIds=TEST-0001&siteIds=TEST-0007");
        Assert.Equal(before, await Snapshot());
    }
}
