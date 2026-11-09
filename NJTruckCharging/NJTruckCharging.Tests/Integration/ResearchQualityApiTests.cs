using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NJTruckCharging.Infrastructure.Persistence;

namespace NJTruckCharging.Tests.Integration;

public sealed class ResearchQualityWebAppFactory : TestWebAppFactory
{
    protected override string DatabaseName => "NJTruckCharging_ResearchQualityTest";

    public async Task ClearResearchAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM ResearchHistoryEntries; DELETE FROM FeasibilityItems; DELETE FROM SiteContacts; " +
            "DELETE FROM SiteScreeningEntries; DELETE FROM SiteScreenings; DELETE FROM SavedSites;");
    }

    /// <summary>Inserts a feasibility row the way records made before Phase 1 look: no source, no evidence date, no history.</summary>
    public async Task InsertLegacyVerdictAsync(string siteId, string itemKey, string status, string evidence)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO FeasibilityItems (SiteId, ItemKey, Status, Evidence, Reviewer, ReviewedAtUtc, CreatedAtUtc, UpdatedAtUtc)
            VALUES ({siteId}, {itemKey}, {status}, {evidence}, NULL, '2026-10-20T00:00:00+00:00', '2026-10-20T00:00:00+00:00', '2026-10-20T00:00:00+00:00')");
    }

    public async Task<int> HistoryCountAsync(string siteId)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.ResearchHistory.CountAsync(h => h.SiteId == siteId);
    }
}

/// <summary>
/// Phase 1 research quality: evidence (source, date, corrections, conflicts) with append-only history, the review
/// queue, and research completeness per site and segment — all independent of the C3 score.
/// Test sites: TEST-n is in segment ((n-1) % 6) + 1.
/// </summary>
public class ResearchQualityApiTests(ResearchQualityWebAppFactory f) : IClassFixture<ResearchQualityWebAppFactory>, IAsyncLifetime
{
    private const string Src = "https://example.org/evidence";
    private const string Date = "2026-10-01";
    private HttpClient C => f.CreateClient();

    public async Task InitializeAsync() { await f.EnsureSeededAsync(); await f.ClearResearchAsync(); }
    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        var body = await r.Content.ReadAsStringAsync();
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement;
    }

    private Task<HttpResponseMessage> Feas(string site, string key, string status, string? evidence = "checked", bool documented = true,
        string? date = Date, string? note = null, string? by = "Tester") =>
        C.PutAsJsonAsync("/api/feasibility", new
        {
            siteId = site, itemKey = key, status, evidence,
            sourceUrl = documented ? Src : null, evidenceDate = documented ? date : null, correctionNote = note, recordedBy = by
        });

    private async Task<JsonElement> Site(string site) => await Json(await C.GetAsync($"/api/research/site?siteId={site}"));
    private async Task<JsonElement> Queue(string q = "") => await Json(await C.GetAsync("/api/research/queue" + q));
    private async Task<List<JsonElement>> History(string site) =>
        (await Json(await C.GetAsync($"/api/research/history?siteId={site}"))).EnumerateArray().ToList();
    private static string[] Issues(JsonElement item) => item.GetProperty("issues").EnumerateArray().Select(i => i.GetProperty("issue").GetString()!).ToArray();

    // ---------------- evidence & history ----------------

    [Fact]
    public async Task Each_save_appends_exactly_one_history_entry_with_its_evidence()
    {
        var r = await Json(await Feas("TEST-0001", "Zoning", "Verified", "Zoning map: I-2 permits warehousing"));
        Assert.Equal(Src, r.GetProperty("sourceUrl").GetString());
        Assert.Equal(Date, r.GetProperty("evidenceDate").GetString());
        Assert.Equal("Tester", r.GetProperty("reviewer").GetString());
        Assert.Equal("Verified", r.GetProperty("evidenceState").GetString());

        var h = Assert.Single(await History("TEST-0001"));
        Assert.Equal("Recorded", h.GetProperty("action").GetString());
        Assert.Equal("Zoning", h.GetProperty("itemKey").GetString());
        Assert.Equal("Unknown", h.GetProperty("previousStatus").GetString());
        Assert.Equal("Verified", h.GetProperty("status").GetString());
        Assert.Equal(Src, h.GetProperty("sourceUrl").GetString());
        Assert.Equal("Tester", h.GetProperty("recordedBy").GetString());

        // Re-saving the identical verdict adds nothing.
        await Json(await Feas("TEST-0001", "Zoning", "Verified", "Zoning map: I-2 permits warehousing"));
        Assert.Equal(1, await f.HistoryCountAsync("TEST-0001"));
    }

    [Fact]
    public async Task Rejected_saves_write_no_history()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await Feas("TEST-0002", "Zoning", "Conflicting", evidence: null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Feas("TEST-0002", "Zoning", "Verified", date: "2999-01-01")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Feas("TEST-0002", "Zoning", "Verified", date: "1850-01-01")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Feas("NO-SUCH-SITE", "Zoning", "Verified")).StatusCode);
        Assert.Equal(0, await f.HistoryCountAsync("TEST-0002"));
        Assert.Equal(0, await f.HistoryCountAsync("NO-SUCH-SITE"));
    }

    [Fact]
    public async Task A_correction_keeps_the_earlier_verdict_in_history()
    {
        await Json(await Feas("TEST-0003", "ElectricalService", "Verified", "Utility letter: 2 MVA"));
        await Json(await Feas("TEST-0003", "ElectricalService", "Concern", "Revised letter: upgrade needed", note: "Utility revised capacity"));

        var h = await History("TEST-0003");
        Assert.Equal(2, h.Count);
        Assert.Equal("Corrected", h[0].GetProperty("action").GetString());
        Assert.Equal("Verified", h[0].GetProperty("previousStatus").GetString());
        Assert.Equal("Concern", h[0].GetProperty("status").GetString());
        Assert.Equal("Utility revised capacity", h[0].GetProperty("note").GetString());
        Assert.Equal("Utility letter: 2 MVA", h[1].GetProperty("evidence").GetString());   // original still visible
    }

    [Fact]
    public async Task Undocumented_verdict_is_kept_but_flagged_not_verified()
    {
        var r = await Json(await Feas("TEST-0004", "TruckEntranceAccess", "Verified", "curb cuts", documented: false));
        Assert.Equal("Verified", r.GetProperty("status").GetString());                   // stored as entered
        Assert.Equal("InsufficientEvidence", r.GetProperty("evidenceState").GetString()); // but flagged

        var site = await Site("TEST-0004");
        Assert.Equal(0, site.GetProperty("reviewed").GetInt32());
        Assert.Equal(0, site.GetProperty("verified").GetInt32());
        Assert.Contains("InsufficientEvidence", Issues(site));
        Assert.DoesNotContain("TruckAccessUnchecked", Issues(site));
    }

    [Fact]
    public async Task Concern_is_reviewed_but_not_verified()
    {
        await Json(await Feas("TEST-0005", "Zoning", "Concern", "Conditional use hearing needed"));
        await Json(await Feas("TEST-0005", "ParcelIdentity", "Verified", "Tax map matches"));
        var site = await Site("TEST-0005");
        Assert.Equal(2, site.GetProperty("reviewed").GetInt32());
        Assert.Equal(1, site.GetProperty("verified").GetInt32());
        var zoning = site.GetProperty("checks").EnumerateArray().Single(c => c.GetProperty("check").GetString() == "Zoning");
        Assert.True(zoning.GetProperty("reviewed").GetBoolean());
        Assert.False(zoning.GetProperty("verified").GetBoolean());
    }

    [Fact]
    public async Task Contact_add_update_and_remove_are_recorded_in_history()
    {
        var created = await Json(await C.PostAsJsonAsync("/api/contacts", new
        {
            siteId = "TEST-0006", name = "Sam Owner", contactType = "PropertyOwner", verificationStatus = "Unverified", recordedBy = "Tester"
        }));
        var id = created.GetProperty("id").GetInt32();
        await Json(await C.PutAsJsonAsync($"/api/contacts/{id}", new
        {
            name = "Sam Owner", contactType = "PropertyOwner", sourceUrl = Src, verificationStatus = "Verified", recordedBy = "Tester"
        }));
        Assert.Equal(HttpStatusCode.NoContent, (await C.DeleteAsync($"/api/contacts/{id}")).StatusCode);

        var h = await History("TEST-0006");
        Assert.Equal(["ContactRemoved", "ContactUpdated", "ContactAdded"], h.Select(x => x.GetProperty("action").GetString()!).ToArray());
        Assert.All(h, x => Assert.Equal(id, x.GetProperty("contactId").GetInt32()));
        Assert.Equal("Unverified", h[1].GetProperty("previousStatus").GetString());
        Assert.Equal("Verified", h[1].GetProperty("status").GetString());
    }

    // ---------------- research set & start research ----------------

    [Fact]
    public async Task Start_research_adds_a_site_once_and_unknown_sites_are_rejected()
    {
        Assert.False((await Site("TEST-0013")).GetProperty("inResearchSet").GetBoolean());
        Assert.DoesNotContain((await Queue()).GetProperty("items").EnumerateArray(), i => i.GetProperty("siteId").GetString() == "TEST-0013");

        var started = await Json(await C.PostAsJsonAsync("/api/research/start", new { siteId = "TEST-0013", recordedBy = "Tester" }));
        Assert.True(started.GetProperty("inResearchSet").GetBoolean());
        await Json(await C.PostAsJsonAsync("/api/research/start", new { siteId = "TEST-0013" }));   // idempotent
        Assert.Equal(1, await f.HistoryCountAsync("TEST-0013"));

        var item = (await Queue()).GetProperty("items").EnumerateArray().Single(i => i.GetProperty("siteId").GetString() == "TEST-0013");
        Assert.Equal(["TruckAccessUnchecked", "ElectricalUnknown"], Issues(item));

        Assert.Equal(HttpStatusCode.NotFound, (await C.PostAsJsonAsync("/api/research/start", new { siteId = "NO-SUCH-SITE" })).StatusCode);
    }

    [Fact]
    public async Task Saved_and_screened_sites_are_in_the_research_set()
    {
        await Json(await C.PostAsJsonAsync("/api/saved-sites", new { siteId = "TEST-0019" }));
        await Json(await C.PutAsJsonAsync("/api/screening", new { siteId = "TEST-0025", decision = "Promising", evidence = "Open yard" }));
        Assert.True((await Site("TEST-0019")).GetProperty("inResearchSet").GetBoolean());
        Assert.True((await Site("TEST-0025")).GetProperty("inResearchSet").GetBoolean());
        Assert.False((await Site("TEST-0031")).GetProperty("inResearchSet").GetBoolean());
    }

    // ---------------- review queue ----------------

    [Fact]
    public async Task Queue_lists_every_issue_type_and_drops_issues_once_resolved_with_evidence()
    {
        await Json(await Feas("TEST-0007", "ParcelIdentity", "Concern", "Lot was split in 2024"));
        await Json(await Feas("TEST-0008", "BusinessAtAddress", "Conflicting", "Listing says A; signage says B"));
        await Json(await Feas("TEST-0009", "Zoning", "Conflicting", "Zoning map vs ordinance disagree"));
        await Json(await C.PostAsJsonAsync("/api/contacts", new { siteId = "TEST-0010", name = "Lead", contactType = "Broker", verificationStatus = "Unverified" }));
        await Json(await Feas("TEST-0011", "FloodEnvironmental", "Verified", "x", documented: false));

        var q = await Queue();
        var counts = q.GetProperty("issueCounts");
        foreach (var issue in new[] { "ParcelMismatch", "BusinessConflict", "ConflictingEvidence", "UnverifiedContact", "TruckAccessUnchecked", "ElectricalUnknown", "InsufficientEvidence" })
            Assert.True(counts.GetProperty(issue).GetInt32() >= 1, issue);

        // Filter by issue.
        var parcel = (await Queue("?issues=ParcelMismatch")).GetProperty("items").EnumerateArray().ToList();
        Assert.Equal("TEST-0007", Assert.Single(parcel).GetProperty("siteId").GetString());

        // Resolve TEST-0007 completely with documented evidence → it leaves the queue.
        await Json(await Feas("TEST-0007", "ParcelIdentity", "Verified", "Corrected block/lot confirmed on tax map", note: "Researcher used the post-split lot"));
        await Json(await Feas("TEST-0007", "TruckEntranceAccess", "Verified", "Curb cut on aerial"));
        await Json(await Feas("TEST-0007", "ElectricalService", "Concern", "Utility: 18-month upgrade"));
        Assert.DoesNotContain((await Queue()).GetProperty("items").EnumerateArray(), i => i.GetProperty("siteId").GetString() == "TEST-0007");
    }

    [Fact]
    public async Task Sites_screened_unsuitable_leave_the_queue()
    {
        await Json(await Feas("TEST-0014", "ParcelIdentity", "Concern", "Radio-tower easement"));
        Assert.Contains((await Queue()).GetProperty("items").EnumerateArray(), i => i.GetProperty("siteId").GetString() == "TEST-0014");
        await Json(await C.PutAsJsonAsync("/api/screening", new { siteId = "TEST-0014", decision = "Unsuitable", evidence = "Easement only" }));
        var q = await Queue();
        Assert.DoesNotContain(q.GetProperty("items").EnumerateArray(), i => i.GetProperty("siteId").GetString() == "TEST-0014");
        Assert.True(q.GetProperty("excludedUnsuitable").GetInt32() >= 1);
    }

    [Fact]
    public async Task Queue_and_summary_filter_by_segment()
    {
        await Json(await C.PostAsJsonAsync("/api/research/start", new { siteId = "TEST-0015" }));   // segment 3
        await Json(await C.PostAsJsonAsync("/api/research/start", new { siteId = "TEST-0016" }));   // segment 4
        var q = await Queue("?segments=3");
        Assert.All(q.GetProperty("items").EnumerateArray(), i => Assert.Equal(3, i.GetProperty("segment").GetInt32()));
        Assert.Contains(q.GetProperty("items").EnumerateArray(), i => i.GetProperty("siteId").GetString() == "TEST-0015");
        var s = await Json(await C.GetAsync("/api/research/summary?segments=4"));
        Assert.Equal(4, Assert.Single(s.GetProperty("segments").EnumerateArray()).GetProperty("segment").GetInt32());
    }

    // ---------------- completeness ----------------

    private async Task CompleteAsync(string site)
    {
        foreach (var key in new[] { "ParcelIdentity", "BusinessAtAddress", "TruckEntranceAccess", "TurningManeuveringSpace", "TruckParkingCapacity", "ElectricalService" })
            await Json(await Feas(site, key, "Verified"));
        await Json(await Feas(site, "Zoning", "Concern", "Hearing needed"));
        await Json(await C.PostAsJsonAsync("/api/contacts", new { siteId = site, name = "Owner", contactType = "PropertyOwner", sourceUrl = Src, verificationStatus = "Verified" }));
    }

    [Fact]
    public async Task Segment_summary_counts_stages_and_checks()
    {
        await CompleteAsync("TEST-0017");                                                          // segment 5: complete
        await Json(await Feas("TEST-0023", "Zoning", "Verified"));                                  // segment 5: partial
        await Json(await C.PostAsJsonAsync("/api/research/start", new { siteId = "TEST-0029" }));   // segment 5: none

        var seg5 = Assert.Single((await Json(await C.GetAsync("/api/research/summary?segments=5"))).GetProperty("segments").EnumerateArray());
        Assert.Equal(3, seg5.GetProperty("researchSites").GetInt32());
        Assert.Equal(1, seg5.GetProperty("allReviewed").GetInt32());
        Assert.Equal(1, seg5.GetProperty("partlyReviewed").GetInt32());
        Assert.Equal(1, seg5.GetProperty("noneReviewed").GetInt32());
        Assert.Equal(9, seg5.GetProperty("checksReviewed").GetInt32());    // 8 + 1
        Assert.Equal(8, seg5.GetProperty("checksVerified").GetInt32());    // 7 (zoning is a concern) + 1
        Assert.Equal(24, seg5.GetProperty("checksTotal").GetInt32());
    }

    [Fact]
    public async Task Completeness_filter_in_sql_matches_the_in_memory_counts()
    {
        await CompleteAsync("TEST-0012");
        await Json(await Feas("TEST-0018", "Zoning", "Verified"));
        await Json(await C.PostAsJsonAsync("/api/research/start", new { siteId = "TEST-0024" }));

        async Task<List<JsonElement>> Rows(string stage) =>
            (await Json(await C.GetAsync($"/api/sites?researchStages={stage}&page=1&pageSize=500"))).GetProperty("items").EnumerateArray().ToList();

        var complete = await Rows("complete");
        Assert.Contains(complete, r => r.GetProperty("siteId").GetString() == "TEST-0012");
        Assert.All(complete, r => Assert.Equal(8, r.GetProperty("researchReviewed").GetInt32()));
        var partial = await Rows("partial");
        Assert.Contains(partial, r => r.GetProperty("siteId").GetString() == "TEST-0018");
        Assert.All(partial, r => Assert.InRange(r.GetProperty("researchReviewed").GetInt32(), 1, 7));
        var none = await Rows("none");
        Assert.Contains(none, r => r.GetProperty("siteId").GetString() == "TEST-0024");
        Assert.All(none, r => Assert.Equal(0, r.GetProperty("researchReviewed").GetInt32()));

        // "any" = the whole research set, and it never includes an untouched site.
        var any = (await Rows("any")).Select(r => r.GetProperty("siteId").GetString()).ToHashSet();
        Assert.Superset(new HashSet<string?> { "TEST-0012", "TEST-0018", "TEST-0024" }, any);
        Assert.DoesNotContain("TEST-0030", any);

        var row = complete.Single(r => r.GetProperty("siteId").GetString() == "TEST-0012");
        Assert.Equal(7, row.GetProperty("researchVerified").GetInt32());
    }

    // ---------------- one evidence rule across every surface ----------------

    /// <summary>What every surface says about a site's parcel check.</summary>
    private sealed record ParcelView(string SiteEvidence, string TableEvidence, bool InVerifiedFilter, bool InInsufficientFilter,
        string ValidationStatus, string SuitabilityBasis, string CheckState, int Reviewed, int Verified, string[] QueueIssues);

    private async Task<ParcelView> ViewAsync(string site)
    {
        async Task<List<JsonElement>> Rows(string q) =>
            (await Json(await C.GetAsync($"/api/sites?search={site}&{q}&page=1&pageSize=5"))).GetProperty("items").EnumerateArray().ToList();
        var row = (await Rows("x=1")).Single(r => r.GetProperty("siteId").GetString() == site);
        var detail = await Json(await C.GetAsync($"/api/sites/{row.GetProperty("id").GetInt32()}"));
        var validation = detail.GetProperty("validation");
        var parcelSection = validation.GetProperty("sections").EnumerateArray().Single(x => x.GetProperty("key").GetString() == "Parcel");
        var identity = detail.GetProperty("suitability").GetProperty("items").EnumerateArray().Single(x => x.GetProperty("key").GetString() == "Identity");
        var research = await Site(site);
        var parcelCheck = research.GetProperty("checks").EnumerateArray().Single(x => x.GetProperty("check").GetString() == "ParcelIdentity");
        var queued = (await Queue()).GetProperty("items").EnumerateArray().SingleOrDefault(i => i.GetProperty("siteId").GetString() == site);
        return new ParcelView(
            validation.GetProperty("siteEvidence").GetString()!,
            row.GetProperty("siteEvidence").GetString()!,
            (await Rows("evidenceStates=verified")).Any(r => r.GetProperty("siteId").GetString() == site),
            (await Rows("evidenceStates=insufficient")).Any(r => r.GetProperty("siteId").GetString() == site),
            parcelSection.GetProperty("status").GetString()!,
            identity.GetProperty("basis").GetString()!,
            parcelCheck.GetProperty("state").GetString()!,
            research.GetProperty("reviewed").GetInt32(), research.GetProperty("verified").GetInt32(),
            queued.ValueKind == JsonValueKind.Undefined ? [] : Issues(queued));
    }

    private static void AssertInsufficient(ParcelView v)
    {
        Assert.Equal("InsufficientEvidence", v.SiteEvidence);
        Assert.Equal("InsufficientEvidence", v.TableEvidence);
        Assert.False(v.InVerifiedFilter);
        Assert.True(v.InInsufficientFilter);
        Assert.Equal("InsufficientEvidence", v.ValidationStatus);
        Assert.Equal("InsufficientEvidence", v.SuitabilityBasis);
        Assert.Equal("InsufficientEvidence", v.CheckState);
        Assert.Equal(0, v.Reviewed);
        Assert.Equal(0, v.Verified);
        Assert.Contains("InsufficientEvidence", v.QueueIssues);
    }

    [Fact]
    public async Task Missing_source_is_insufficient_evidence_everywhere()
    {
        Assert.Equal(HttpStatusCode.OK, (await C.PutAsJsonAsync("/api/feasibility", new
        {
            siteId = "TEST-0037", itemKey = "ParcelIdentity", status = "Verified", evidence = "Tax map matches", evidenceDate = Date
        })).StatusCode);
        AssertInsufficient(await ViewAsync("TEST-0037"));
    }

    [Fact]
    public async Task Missing_evidence_date_is_insufficient_evidence_everywhere()
    {
        Assert.Equal(HttpStatusCode.OK, (await C.PutAsJsonAsync("/api/feasibility", new
        {
            siteId = "TEST-0038", itemKey = "ParcelIdentity", status = "Verified", evidence = "Tax map matches", sourceUrl = Src
        })).StatusCode);
        AssertInsufficient(await ViewAsync("TEST-0038"));
    }

    [Fact]
    public async Task Legacy_record_is_preserved_and_labelled_insufficient_evidence()
    {
        await f.InsertLegacyVerdictAsync("TEST-0039", "ParcelIdentity", "Verified", "Tax map matches (pre-Phase 1)");
        AssertInsufficient(await ViewAsync("TEST-0039"));

        // Kept exactly as recorded — not deleted, not upgraded, no history invented for it.
        var item = (await Json(await C.GetAsync("/api/feasibility?siteId=TEST-0039"))).EnumerateArray()
            .Single(i => i.GetProperty("itemKey").GetString() == "ParcelIdentity");
        Assert.Equal("Verified", item.GetProperty("status").GetString());
        Assert.Equal("Tax map matches (pre-Phase 1)", item.GetProperty("evidence").GetString());
        Assert.Equal("InsufficientEvidence", item.GetProperty("evidenceState").GetString());
        Assert.Equal(0, await f.HistoryCountAsync("TEST-0039"));

        // Adding the source and date later makes it a documented verification.
        await Json(await Feas("TEST-0039", "ParcelIdentity", "Verified", "Tax map matches (pre-Phase 1)"));
        Assert.Equal("Verified", (await ViewAsync("TEST-0039")).SiteEvidence);
    }

    [Fact]
    public async Task Conflicting_evidence_is_a_mismatch_everywhere()
    {
        await Json(await Feas("TEST-0040", "ParcelIdentity", "Conflicting", "Tax map says lot 1; deed says lot 1.01"));
        var v = await ViewAsync("TEST-0040");
        Assert.Equal("Mismatch", v.SiteEvidence);
        Assert.Equal("Mismatch", v.TableEvidence);
        Assert.False(v.InVerifiedFilter);
        Assert.Equal("Mismatch", v.ValidationStatus);
        Assert.Equal("Conflicting", v.SuitabilityBasis);
        Assert.Equal("Conflicting", v.CheckState);
        Assert.Equal(0, v.Reviewed);
        Assert.Contains("ParcelMismatch", v.QueueIssues);
    }

    [Fact]
    public async Task Valid_verification_is_verified_everywhere()
    {
        await Json(await Feas("TEST-0041", "ParcelIdentity", "Verified", "Block/lot matches the county tax map"));
        var v = await ViewAsync("TEST-0041");
        Assert.Equal("Verified", v.SiteEvidence);
        Assert.Equal("Verified", v.TableEvidence);
        Assert.True(v.InVerifiedFilter);
        Assert.False(v.InInsufficientFilter);
        Assert.Equal("Verified", v.ValidationStatus);
        Assert.Equal("Verified", v.SuitabilityBasis);
        Assert.Equal("Verified", v.CheckState);
        Assert.Equal(1, v.Reviewed);
        Assert.Equal(1, v.Verified);
        Assert.DoesNotContain("InsufficientEvidence", v.QueueIssues);
        Assert.DoesNotContain("ParcelMismatch", v.QueueIssues);
    }

    [Fact]
    public async Task Verified_filters_and_business_confirmation_require_documented_evidence()
    {
        await f.InsertLegacyVerdictAsync("TEST-0043", "TruckEntranceAccess", "Verified", "curb cuts");
        await f.InsertLegacyVerdictAsync("TEST-0043", "BusinessAtAddress", "Verified", "signage");
        await Json(await Feas("TEST-0044", "TruckEntranceAccess", "Verified", "Two curb cuts"));
        await Json(await Feas("TEST-0044", "BusinessAtAddress", "Verified", "Signage matches"));

        async Task<JsonElement?> Row(string site, string q = "x=1") =>
            (await Json(await C.GetAsync($"/api/sites?search={site}&{q}&page=1&pageSize=5"))).GetProperty("items").EnumerateArray()
                .Cast<JsonElement?>().SingleOrDefault(r => r!.Value.GetProperty("siteId").GetString() == site);
        Assert.Null(await Row("TEST-0043", "truckEntranceVerified=true"));
        Assert.NotNull(await Row("TEST-0044", "truckEntranceVerified=true"));
        Assert.False((await Row("TEST-0043"))!.Value.GetProperty("businessListingConfirmed").GetBoolean());
        Assert.True((await Row("TEST-0044"))!.Value.GetProperty("businessListingConfirmed").GetBoolean());

        var detail = await Json(await C.GetAsync($"/api/sites/{(await Row("TEST-0043"))!.Value.GetProperty("id").GetInt32()}"));
        Assert.Equal("InsufficientEvidence", detail.GetProperty("truckEntranceEvidenceState").GetString());
        Assert.Equal("Verified", detail.GetProperty("truckEntranceStatus").GetString());   // stored verdict unchanged
    }

    // ---------------- independence from the C3 score ----------------

    [Fact]
    public async Task Research_activity_never_changes_scores_or_ranks()
    {
        async Task<Dictionary<string, (double, int)>> Snapshot() =>
            (await Json(await C.GetAsync("/api/sites?segments=1&page=1&pageSize=500&sort=Rank"))).GetProperty("items").EnumerateArray()
                .ToDictionary(r => r.GetProperty("siteId").GetString()!, r => (r.GetProperty("score").GetDouble(), r.GetProperty("rank").GetInt32()));

        var before = await Snapshot();
        await CompleteAsync("TEST-0001");
        await Json(await Feas("TEST-0007", "ParcelIdentity", "Concern", "Mismatch"));
        await Json(await C.PostAsJsonAsync("/api/research/start", new { siteId = "TEST-0013" }));
        var after = await Snapshot();
        Assert.Equal(before, after);
    }
}
