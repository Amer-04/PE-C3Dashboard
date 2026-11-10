using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace NJTruckCharging.Tests.Integration;

/// <summary>
/// Integration tests against the real app + a LocalDB test database. The application has no
/// authentication: all endpoints are open. Covers open access, saved-site CRUD, contacts,
/// feasibility, and ranked-list query performance.
/// </summary>
public partial class ApiIntegrationTests(TestWebAppFactory factory) : IClassFixture<TestWebAppFactory>, IAsyncLifetime
{
    private readonly TestWebAppFactory _factory = factory;

    public async Task InitializeAsync()
    {
        await _factory.EnsureSeededAsync();
        await _factory.ClearSavedAsync();
        await _factory.ClearContactsAsync();
        await _factory.ClearFeasibilityAsync();
        await _factory.ClearScreeningAsync();
    }
    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient Client() => _factory.CreateClient();
    private static readonly int FeasibilityKeyCount = Enum.GetValues<NJTruckCharging.Domain.Entities.FeasibilityItemKey>().Length;

    // ---- Open access (no authentication) ----

    [Fact]
    public async Task Dashboard_endpoints_are_open_without_login()
    {
        var client = Client();
        // Core C3 endpoints respond 200 with no auth.
        foreach (var path in new[] { "/api/sites?page=1&pageSize=5", "/api/sites/map", "/api/dashboard/summary", "/api/methodology" })
        {
            var resp = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        }
        var sites = await (await client.GetAsync("/api/sites?page=1&pageSize=5")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(sites.GetProperty("items").GetArrayLength() > 0);
    }

    [Fact]
    public async Task No_auth_endpoints_exist()
    {
        var client = Client();
        var resp = await client.PostAsJsonAsync("/api/auth/login", new { email = "x@y.z", password = "whatever" });
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode); // the auth controller was removed
    }

    // ---- Saved-site bookmarking ----

    [Fact]
    public async Task Bookmark_is_idempotent_supports_notes_and_delete()
    {
        var client = Client();
        var siteId = _factory.SampleSiteId;

        var create = await client.PostAsJsonAsync("/api/saved-sites", new { siteId });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetInt32();
        Assert.True(created.GetProperty("candidateFound").GetBoolean());

        // Idempotent: bookmarking again returns 200, not a duplicate.
        var again = await client.PostAsJsonAsync("/api/saved-sites", new { siteId });
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        var list = await (await client.GetAsync("/api/saved-sites")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, list.GetArrayLength());

        // Edit the note.
        var put = await client.PutAsJsonAsync($"/api/saved-sites/{id}", new { notes = "Great access to I-95" });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var updated = await put.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Great access to I-95", updated.GetProperty("notes").GetString());

        // No pipeline/CRM fields remain on the DTO.
        foreach (var gone in new[] { "status", "assignedTo", "nextAction", "transferredAtUtc", "crmItemId" })
            Assert.False(updated.TryGetProperty(gone, out _), $"DTO should not expose '{gone}'");

        // Delete.
        var del = await client.DeleteAsync($"/api/saved-sites/{id}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);
        var empty = await (await client.GetAsync("/api/saved-sites")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, empty.GetArrayLength());
    }

    [Fact]
    public async Task Saving_unknown_site_returns_404()
    {
        var client = Client();
        var resp = await client.PostAsJsonAsync("/api/saved-sites", new { siteId = "NO-SUCH-SITE" });
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    // ---- Contact intelligence ----

    [Fact]
    public async Task Contact_crud_and_verification_flow()
    {
        var client = Client();
        var siteId = _factory.SampleSiteId;

        var create = await client.PostAsJsonAsync("/api/contacts", new
        {
            siteId, name = "Jane Owner", organization = "Acme Logistics", jobTitle = "Facilities Director",
            contactType = "PropertyOwner", businessPhone = "973-555-0100", email = "jane@acme.example",
            sourceUrl = "https://acme.example/about", researchNotes = "Found via county records."
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var dto = await create.Content.ReadFromJsonAsync<JsonElement>();
        var id = dto.GetProperty("id").GetInt32();
        Assert.Equal("Unverified", dto.GetProperty("verificationStatus").GetString());

        var sum1 = await (await client.GetAsync($"/api/contacts/summary?siteId={siteId}")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, sum1.GetProperty("unverified").GetInt32());
        Assert.Equal("PotentialUnverified", sum1.GetProperty("state").GetString());

        var put = await client.PutAsJsonAsync($"/api/contacts/{id}", new
        {
            name = "Jane Owner", organization = "Acme Logistics", jobTitle = "Facilities Director",
            contactType = "PropertyOwner", businessPhone = "973-555-0100", email = "jane@acme.example",
            sourceUrl = "https://acme.example/about", verificationStatus = "Verified", researchNotes = "Confirmed by phone."
        });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var verified = await put.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Verified", verified.GetProperty("verificationStatus").GetString());
        Assert.NotEqual(JsonValueKind.Null, verified.GetProperty("lastVerifiedUtc").ValueKind);

        var sum2 = await (await client.GetAsync($"/api/contacts/summary?siteId={siteId}")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("VerifiedDecisionMaker", sum2.GetProperty("state").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/contacts/{id}")).StatusCode);
        var list = await (await client.GetAsync($"/api/contacts?siteId={siteId}")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, list.GetArrayLength());
    }

    [Fact]
    public async Task Contact_for_unknown_site_returns_404()
    {
        var client = Client();
        var resp = await client.PostAsJsonAsync("/api/contacts", new { siteId = "NO-SUCH-SITE", name = "X", contactType = "Other" });
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    // ---- Feasibility checklist ----

    [Fact]
    public async Task Feasibility_checklist_has_every_item_defaulting_to_unknown()
    {
        var client = Client();
        var items = await (await client.GetAsync($"/api/feasibility?siteId={_factory.SampleSiteId}")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(FeasibilityKeyCount, items.GetArrayLength());
        Assert.All(items.EnumerateArray(), i => Assert.Equal("Unknown", i.GetProperty("status").GetString()));
    }

    [Fact]
    public async Task Feasibility_upsert_records_verdict_and_evidence()
    {
        var client = Client();
        var siteId = _factory.SampleSiteId;

        var put = await client.PutAsJsonAsync("/api/feasibility", new
        {
            siteId, itemKey = "TruckEntranceAccess", status = "Verified", sourceUrl = "https://example.org/evidence", evidenceDate = "2026-10-01", recordedBy = "Tester", evidence = "Two 40ft curb cuts on the frontage road."
        });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var dto = await put.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Verified", dto.GetProperty("status").GetString());
        Assert.NotEqual(JsonValueKind.Null, dto.GetProperty("reviewedAtUtc").ValueKind);

        var items = await (await client.GetAsync($"/api/feasibility?siteId={siteId}")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(FeasibilityKeyCount, items.GetArrayLength());
        var entrance = items.EnumerateArray().First(i => i.GetProperty("itemKey").GetString() == "TruckEntranceAccess");
        Assert.Equal("Verified", entrance.GetProperty("status").GetString());
        Assert.Contains("curb cuts", entrance.GetProperty("evidence").GetString());

        var sum = await (await client.GetAsync($"/api/feasibility/summary?siteId={siteId}")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, sum.GetProperty("verified").GetInt32());
        Assert.Equal(FeasibilityKeyCount - 1, sum.GetProperty("unknown").GetInt32());
    }

    [Fact]
    public async Task Feasibility_for_unknown_site_returns_404()
    {
        var client = Client();
        var resp = await client.PutAsJsonAsync("/api/feasibility", new { siteId = "NO-SUCH-SITE", itemKey = "Zoning", status = "Concern" });
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    // ---- Business-owner screening filters ----

    [Fact]
    public async Task Ios_freight_activity_is_indicated_not_confirmed()
    {
        var client = Client();
        var body = await (await client.GetAsync("/api/sites?iosClasses=indicated&page=1&pageSize=500")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("totalCount").GetInt32() > 0);
        foreach (var it in body.GetProperty("items").EnumerateArray())
        {
            Assert.Equal("FreightIndicated", it.GetProperty("iosClassification").GetString());
            Assert.Equal("SourceDerived", it.GetProperty("iosBasis").GetString());
            Assert.True(it.GetProperty("freight").GetBoolean());
            Assert.True(it.GetProperty("openAcres").GetDouble() >= 1.0);
        }
        // Without any manual review nothing is Confirmed — freight activity is never silently "verified IOS".
        var confirmed = await (await client.GetAsync("/api/sites?iosClasses=confirmed&page=1&pageSize=5")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, confirmed.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Ios_confirmed_only_through_manual_review_with_evidence()
    {
        var client = Client();
        const string siteId = "TEST-0006"; // i=5: freight + 2.5 open ac on commercial land → FreightIndicated

        // Verified without evidence is rejected.
        var noEvidence = await client.PutAsJsonAsync("/api/feasibility", new { siteId, itemKey = "OutdoorStorageUse", status = "Verified" });
        Assert.Equal(HttpStatusCode.BadRequest, noEvidence.StatusCode);

        var ok = await client.PutAsJsonAsync("/api/feasibility", new { siteId, itemKey = "OutdoorStorageUse", status = "Verified", sourceUrl = "https://example.org/evidence", evidenceDate = "2026-10-01", recordedBy = "Tester", evidence = "Site visit: ~30 trailers stored on gravel yard" });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        // Filter, list row, detail and CSV all agree.
        var list = await (await client.GetAsync("/api/sites?iosClasses=confirmed&page=1&pageSize=50")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, list.GetProperty("totalCount").GetInt32());
        var row = list.GetProperty("items")[0];
        Assert.Equal(siteId, row.GetProperty("siteId").GetString());
        Assert.Equal("Confirmed", row.GetProperty("iosClassification").GetString());
        Assert.Equal("ManualReview", row.GetProperty("iosBasis").GetString());

        var indicated = await (await client.GetAsync($"/api/sites?iosClasses=indicated&search={siteId}&page=1&pageSize=50")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, indicated.GetProperty("totalCount").GetInt32()); // no longer counted as merely indicated

        var detail = await (await client.GetAsync($"/api/sites/{row.GetProperty("id").GetInt32()}")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Confirmed", detail.GetProperty("summary").GetProperty("iosClassification").GetString());
        Assert.Equal("FreightIndicated", detail.GetProperty("iosDerivedClassification").GetString());
        Assert.Contains("30 trailers", detail.GetProperty("iosEvidence").GetString());

        var csv = await (await client.GetAsync("/api/sites/export?iosClasses=confirmed")).Content.ReadAsStringAsync();
        var line = csv.Split('\n').Single(l => l.Contains(siteId));
        Assert.Contains(",confirmed,manual review,", line);

        // A concern makes it Not IOS (manual), and the classes still partition the data set.
        await client.PutAsJsonAsync("/api/feasibility", new { siteId, itemKey = "OutdoorStorageUse", status = "Concern", evidence = "Yard is leased to a retailer" });
        var total = 0;
        foreach (var c in new[] { "confirmed", "indicated", "potential", "not", "unknown" })
            total += (await (await client.GetAsync($"/api/sites?iosClasses={c}&page=1&pageSize=1")).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("totalCount").GetInt32();
        var all = (await (await client.GetAsync("/api/sites?page=1&pageSize=1")).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("totalCount").GetInt32();
        Assert.Equal(all, total);
        var notRow = (await (await client.GetAsync($"/api/sites?iosClasses=not&search={siteId}&page=1&pageSize=5")).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items")[0];
        Assert.Equal("ManualReview", notRow.GetProperty("iosBasis").GetString());
    }

    [Fact]
    public async Task Ios_list_rows_match_csv_for_every_class()
    {
        var client = Client();
        var csv = await (await client.GetAsync("/api/sites/export")).Content.ReadAsStringAsync();
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToList();
        var header = lines[0].TrimStart('\uFEFF').Split(',');
        int idCol = Array.IndexOf(header, "site_id"), iosCol = Array.IndexOf(header, "ios_classification");
        // Simple rows (no quoted commas before the IOS column) are enough to cross-check.
        var csvClass = lines.Skip(1).Select(l => l.Split(',')).Where(c => c.Length > iosCol)
            .ToDictionary(c => c[idCol], c => c[iosCol]);

        var page = await (await client.GetAsync("/api/sites?page=1&pageSize=500")).Content.ReadFromJsonAsync<JsonElement>();
        foreach (var it in page.GetProperty("items").EnumerateArray())
        {
            var token = it.GetProperty("iosClassification").GetString() switch
            {
                "Confirmed" => "confirmed", "FreightIndicated" => "indicated", "Potential" => "potential", "NotIos" => "not", _ => "unknown"
            };
            Assert.Equal(token, csvClass[it.GetProperty("siteId").GetString()!]);
        }
    }

    [Fact]
    public async Task Ios_potential_is_suitable_land_without_confirmed_use()
    {
        var client = Client();
        var body = await (await client.GetAsync("/api/sites?iosClasses=potential&page=1&pageSize=500")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("totalCount").GetInt32() > 0);
        foreach (var it in body.GetProperty("items").EnumerateArray())
        {
            Assert.Equal("Potential", it.GetProperty("iosClassification").GetString());
            Assert.False(it.GetProperty("freight").GetBoolean(), "Potential has no on-site freight signal");
            Assert.Equal("SourceDerived", it.GetProperty("iosBasis").GetString());
            var cls = it.GetProperty("propClass").GetString()!;
            Assert.True(cls.Contains("4B") || cls == "1" || cls.StartsWith("1/"));
        }
    }

    [Fact]
    public async Task Ios_unknown_when_no_open_acreage_estimate()
    {
        var client = Client();
        var body = await (await client.GetAsync("/api/sites?iosClasses=unknown&page=1&pageSize=500")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("totalCount").GetInt32() > 0);
        foreach (var it in body.GetProperty("items").EnumerateArray())
        {
            Assert.Equal("Unknown", it.GetProperty("iosClassification").GetString());
            Assert.True(it.GetProperty("openAcres").GetDouble() <= 0);
            Assert.Equal(JsonValueKind.Null, it.GetProperty("estimatedParking").ValueKind); // Unknown parking too
        }
    }

    [Fact]
    public async Task Filter_min_estimated_parking_uses_the_preliminary_estimate()
    {
        var client = Client();
        var body = await (await client.GetAsync("/api/sites?minEstimatedParking=60&page=1&pageSize=500")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("totalCount").GetInt32() < TestWebAppFactory.SeededSiteCount);
        foreach (var it in body.GetProperty("items").EnumerateArray())
        {
            Assert.Equal(JsonValueKind.Number, it.GetProperty("estimatedParking").ValueKind); // never Unknown when a floor is set
            Assert.True(it.GetProperty("estimatedParking").GetInt32() >= 60, "each row meets the estimated-parking floor");
        }
    }

    [Fact]
    public async Task Filtering_does_not_change_scores_or_rankings()
    {
        var client = Client();
        var unfiltered = await (await client.GetAsync("/api/sites?page=1&pageSize=10&sortBy=Score&descending=true")).Content.ReadFromJsonAsync<JsonElement>();
        // minScore=0 keeps every site but exercises the filter pipeline.
        var filtered = await (await client.GetAsync("/api/sites?page=1&pageSize=10&sortBy=Score&descending=true&minScore=0")).Content.ReadFromJsonAsync<JsonElement>();

        var a = unfiltered.GetProperty("items").EnumerateArray().ToList();
        var b = filtered.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(a.Count, b.Count);
        for (var i = 0; i < a.Count; i++)
        {
            Assert.Equal(a[i].GetProperty("siteId").GetString(), b[i].GetProperty("siteId").GetString());
            Assert.Equal(a[i].GetProperty("score").GetDouble(), b[i].GetProperty("score").GetDouble());
            Assert.Equal(a[i].GetProperty("rank").GetInt32(), b[i].GetProperty("rank").GetInt32());
        }
    }

    [Fact]
    public async Task Filter_truck_entrance_verified_matches_feasibility_state()
    {
        var client = Client();
        var siteId = _factory.SampleSiteId;
        // No feasibility yet → filter yields nothing.
        var before = await (await client.GetAsync("/api/sites?truckEntranceVerified=true&page=1&pageSize=50")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, before.GetProperty("totalCount").GetInt32());

        // Verify truck entrance for one site.
        await client.PutAsJsonAsync("/api/feasibility", new { siteId, itemKey = "TruckEntranceAccess", status = "Verified", sourceUrl = "https://example.org/evidence", evidenceDate = "2026-10-01", recordedBy = "Tester", evidence = "curb cuts" });

        var after = await (await client.GetAsync("/api/sites?truckEntranceVerified=true&page=1&pageSize=50")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, after.GetProperty("totalCount").GetInt32());
        Assert.Equal(siteId, after.GetProperty("items")[0].GetProperty("siteId").GetString());
    }

    [Fact]
    public async Task Filter_only_with_verified_contact_matches_contacts()
    {
        var client = Client();
        var siteId = _factory.SampleSiteId;
        var none = await (await client.GetAsync("/api/sites?onlyWithVerifiedContact=true&page=1&pageSize=50")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, none.GetProperty("totalCount").GetInt32());

        await client.PostAsJsonAsync("/api/contacts", new { siteId, name = "Owner", contactType = "PropertyOwner", verificationStatus = "Verified", sourceUrl = "https://example.org/owner" });

        var one = await (await client.GetAsync("/api/sites?onlyWithVerifiedContact=true&page=1&pageSize=50")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, one.GetProperty("totalCount").GetInt32());
        Assert.Equal(siteId, one.GetProperty("items")[0].GetProperty("siteId").GetString());
    }

    [Fact]
    public async Task Evaluation_filters_also_apply_to_dashboard_summary()
    {
        var client = Client();
        await client.PostAsJsonAsync("/api/contacts", new { siteId = _factory.SampleSiteId, name = "Owner", contactType = "PropertyOwner", verificationStatus = "Verified", sourceUrl = "https://example.org/owner" });
        var summary = await (await client.GetAsync("/api/dashboard/summary?onlyWithVerifiedContact=true")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, summary.GetProperty("kpis").GetProperty("sites").GetInt32());
    }

    [Theory]
    [InlineData("Score", true)]
    [InlineData("OpenAcres", false)]     // only 10 distinct values in the seed → heavy ties
    [InlineData("Rank", false)]
    public async Task Paging_is_stable_when_sort_values_tie(string sortBy, bool descending)
    {
        var client = Client();
        var seen = new List<string>();
        int total, pages, page = 1;
        do
        {
            var body = await (await client.GetAsync($"/api/sites?page={page}&pageSize=137&sortBy={sortBy}&descending={descending}"))
                .Content.ReadFromJsonAsync<JsonElement>();
            total = body.GetProperty("totalCount").GetInt32();
            pages = body.GetProperty("totalPages").GetInt32();
            seen.AddRange(body.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("siteId").GetString()!));
        } while (++page <= pages);

        Assert.Equal(total, seen.Count);
        Assert.Equal(total, seen.Distinct().Count()); // no site repeated (and therefore none skipped)
    }

    // ---- Performance (actual provider) ----

    [Fact]
    public async Task Ranked_list_query_is_fast()
    {
        var client = Client();
        await client.GetAsync("/api/sites?page=1&pageSize=25"); // ignore first (JIT/connection warm)

        var sw = Stopwatch.StartNew();
        var resp = await client.GetAsync("/api/sites?page=1&pageSize=25");
        sw.Stop();

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(25, body.GetProperty("items").GetArrayLength());
        Assert.True(body.GetProperty("totalCount").GetInt32() >= TestWebAppFactory.SeededSiteCount);
        Assert.True(sw.ElapsedMilliseconds < 3000, $"Ranked list took {sw.ElapsedMilliseconds}ms over {TestWebAppFactory.SeededSiteCount} rows (expected < 3000ms).");
    }
}
