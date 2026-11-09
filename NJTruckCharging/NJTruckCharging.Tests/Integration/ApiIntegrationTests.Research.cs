using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using NJTruckCharging.Application.Sites;

namespace NJTruckCharging.Tests.Integration;

/// <summary>Site Evidence / Contact Readiness columns + filters, validation sections and the research shortlist export.</summary>
public partial class ApiIntegrationTests
{
    private const string Src = "https://example.org/published-profile";

    /// <summary>Puts one site in every Site Evidence and Contact Readiness state.</summary>
    private static async Task SeedResearchStates(HttpClient c)
    {
        async Task Feas(string siteId, string key, string status, string evidence) =>
            Assert.Equal(HttpStatusCode.OK, (await c.PutAsJsonAsync("/api/feasibility", new { siteId, itemKey = key, status, evidence })).StatusCode);
        async Task Contact(string siteId, string name, string type, string vs, string? src) =>
            Assert.Equal(HttpStatusCode.Created, (await c.PostAsJsonAsync("/api/contacts", new
            {
                siteId, name, contactType = type, verificationStatus = vs, sourceUrl = src,
                organization = "Acme Realty", jobTitle = "Director of Facilities", businessPhone = "555-0100", email = "pat@acme.example"
            })).StatusCode);

        // Site evidence: 0002 verified parcel, 0003 business mismatch, 0004 preliminary (screening only), 0005 preliminary (other check)
        await Feas("TEST-0002", "ParcelIdentity", "Verified", "Block/lot matches county tax map https://tax.example/b749-l1 checked");
        await Feas("TEST-0003", "ParcelIdentity", "Verified", "Tax map matches");
        await Feas("TEST-0003", "BusinessAtAddress", "Concern", "Listed business moved out in 2025 (Street View, signage)");
        Assert.Equal(HttpStatusCode.OK, (await c.PutAsJsonAsync("/api/screening",
            new { siteId = "TEST-0004", decision = "NeedsMoreResearch", evidence = "Yard visible on satellite; confirm entrance" })).StatusCode);
        await Feas("TEST-0005", "ElectricalService", "Concern", "Utility: 18-month upgrade");

        // Contact readiness: 0002 relevant verified, 0003 general verified, 0004 lead, 0005 verified owner WITHOUT source is impossible (400) → lead
        await Contact("TEST-0002", "Pat Facilities", "FacilityManager", "Verified", Src);
        await Contact("TEST-0003", "Main line", "Other", "Verified", Src);
        await Contact("TEST-0004", "Possible broker", "Broker", "Unverified", null);
    }

    [Fact]
    public async Task Research_columns_and_filters_agree_and_partition_every_site()
    {
        var client = Client();
        await SeedResearchStates(client);
        var all = await TotalFor(client, "x=1");

        var expected = new Dictionary<string, (string Evidence, string Contact)>
        {
            ["TEST-0002"] = ("Verified", "RelevantContactVerified"),
            ["TEST-0003"] = ("Mismatch", "GeneralBusinessContact"),
            ["TEST-0004"] = ("Preliminary", "UnverifiedLead"),
            ["TEST-0005"] = ("Preliminary", "NoContact"),
            ["TEST-0006"] = ("Unknown", "NoContact"),
        };

        // Each filter token returns only rows in that state (SQL filter == in-memory column), and the tokens partition all sites.
        var evidenceTotal = 0;
        foreach (var (token, state) in new[] { ("verified", "Verified"), ("preliminary", "Preliminary"), ("mismatch", "Mismatch"), ("unknown", "Unknown") })
        {
            var page = await Json(await client.GetAsync($"/api/sites?evidenceStates={token}&page=1&pageSize=500"));
            evidenceTotal += page.GetProperty("totalCount").GetInt32();
            foreach (var i in page.GetProperty("items").EnumerateArray())
                Assert.Equal(state, i.GetProperty("siteEvidence").GetString());
        }
        Assert.Equal(all, evidenceTotal);

        var contactTotal = 0;
        foreach (var (token, state) in new[] { ("relevant", "RelevantContactVerified"), ("general", "GeneralBusinessContact"), ("lead", "UnverifiedLead"), ("none", "NoContact") })
        {
            var page = await Json(await client.GetAsync($"/api/sites?contactStates={token}&page=1&pageSize=500"));
            contactTotal += page.GetProperty("totalCount").GetInt32();
            foreach (var i in page.GetProperty("items").EnumerateArray())
                Assert.Equal(state, i.GetProperty("contactReadiness").GetString());
        }
        Assert.Equal(all, contactTotal);

        foreach (var (siteId, (ev, ct)) in expected)
        {
            var row = (await Json(await client.GetAsync($"/api/sites?search={siteId}&page=1&pageSize=5"))).GetProperty("items")
                .EnumerateArray().Single(i => i.GetProperty("siteId").GetString() == siteId);
            Assert.Equal(ev, row.GetProperty("siteEvidence").GetString());
            Assert.Equal(ct, row.GetProperty("contactReadiness").GetString());
        }

        // Combined filters, and the existing "verified decision-maker" filter now means a sourced, relevant role only.
        Assert.Equal(1, await TotalFor(client, "evidenceStates=verified&contactStates=relevant"));
        Assert.Equal(1, await TotalFor(client, "onlyWithVerifiedContact=true"));
        Assert.Equal(1, await TotalFor(client, "contactStates=general"));
    }

    [Fact]
    public async Task High_score_alone_never_makes_a_site_verified()
    {
        var client = Client();
        var top = await Json(await client.GetAsync("/api/sites?sortBy=Score&descending=true&page=1&pageSize=20"));
        foreach (var i in top.GetProperty("items").EnumerateArray())
        {
            Assert.Equal("Unknown", i.GetProperty("siteEvidence").GetString());
            Assert.Equal("NoContact", i.GetProperty("contactReadiness").GetString());
        }
        Assert.Equal(0, await TotalFor(client, "evidenceStates=verified"));
        Assert.Equal(0, await TotalFor(client, "contactStates=relevant"));
    }

    [Fact]
    public async Task Verification_requires_supporting_evidence()
    {
        var client = Client();
        // A contact cannot be marked Verified without a source …
        var noSrc = await client.PostAsJsonAsync("/api/contacts", new { siteId = "TEST-0002", name = "Owner", contactType = "PropertyOwner", verificationStatus = "Verified" });
        Assert.Equal(HttpStatusCode.BadRequest, noSrc.StatusCode);
        var lead = await (await client.PostAsJsonAsync("/api/contacts", new { siteId = "TEST-0002", name = "Owner", contactType = "PropertyOwner" }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var id = lead.GetProperty("id").GetInt32();
        var upd = await client.PutAsJsonAsync($"/api/contacts/{id}", new { name = "Owner", contactType = "PropertyOwner", verificationStatus = "Verified" });
        Assert.Equal(HttpStatusCode.BadRequest, upd.StatusCode);
        var ok = await client.PutAsJsonAsync($"/api/contacts/{id}", new { name = "Owner", contactType = "PropertyOwner", verificationStatus = "Verified", sourceUrl = Src });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        // … and parcel / business-at-address verdicts need evidence text.
        foreach (var key in new[] { "ParcelIdentity", "BusinessAtAddress" })
        foreach (var status in new[] { "Verified", "Concern" })
        {
            var r = await client.PutAsJsonAsync("/api/feasibility", new { siteId = "TEST-0002", itemKey = key, status, evidence = " " });
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        }
    }

    [Fact]
    public async Task Site_detail_separates_parcel_business_owner_and_suitability()
    {
        var client = Client();
        var id = await CandidateIdOf(client, "TEST-0006");
        var bare = (await Json(await client.GetAsync($"/api/sites/{id}"))).GetProperty("validation");
        Assert.Equal("Unknown", bare.GetProperty("siteEvidence").GetString());
        var keys = bare.GetProperty("sections").EnumerateArray().Select(s => s.GetProperty("key").GetString()!).ToArray();
        Assert.Equal(["Parcel", "Business", "Owner", "Suitability"], keys);
        foreach (var s in bare.GetProperty("sections").EnumerateArray())
        {
            Assert.NotEqual("Verified", s.GetProperty("status").GetString()); // nothing verified without research
            Assert.Equal(JsonValueKind.Null, s.GetProperty("checkedAtUtc").ValueKind);
        }
        Assert.True(bare.GetProperty("missingVerification").GetArrayLength() >= 4);

        await SeedResearchStates(client);
        var v = (await Json(await client.GetAsync($"/api/sites/{await CandidateIdOf(client, "TEST-0003")}"))).GetProperty("validation");
        JsonElement Sec(string k) => v.GetProperty("sections").EnumerateArray().Single(s => s.GetProperty("key").GetString() == k);
        Assert.Equal("Mismatch", v.GetProperty("siteEvidence").GetString());
        Assert.Equal("Verified", Sec("Parcel").GetProperty("status").GetString());
        Assert.Equal("Mismatch", Sec("Business").GetProperty("status").GetString());
        Assert.Contains(Sec("Business").GetProperty("discrepancies").EnumerateArray(), d => d.GetString()!.Contains("moved out"));
        Assert.NotEqual(JsonValueKind.Null, Sec("Business").GetProperty("checkedAtUtc").ValueKind);
        Assert.NotEqual("Verified", Sec("Owner").GetProperty("status").GetString()); // a general line is not the owner

        var v2 = (await Json(await client.GetAsync($"/api/sites/{await CandidateIdOf(client, "TEST-0002")}"))).GetProperty("validation");
        var parcel = v2.GetProperty("sections").EnumerateArray().Single(s => s.GetProperty("key").GetString() == "Parcel");
        Assert.Equal("https://tax.example/b749-l1", parcel.GetProperty("evidenceLinks")[0].GetString());
    }

    [Fact]
    public async Task Research_export_has_requested_columns_preserves_unknowns_and_attributes_sources()
    {
        var client = Client();
        await SeedResearchStates(client);
        var resp = await client.GetAsync("/api/sites/export?format=research&search=TEST-000");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Contains("research-shortlist", resp.Content.Headers.ContentDisposition!.FileName);
        var lines = ParseCsv(Encoding.UTF8.GetString(await resp.Content.ReadAsByteArrayAsync()).TrimStart('﻿'));
        var header = lines[0];
        Assert.Equal(SiteQueryService.ResearchColumns, header);
        var rows = lines.Skip(1).Select(r => header.Zip(r).ToDictionary(p => p.First, p => p.Second)).ToDictionary(r => r["site_id"]);
        Assert.Equal(9, rows.Count); // TEST-0001..0009

        var bare = rows["TEST-0006"];
        Assert.Equal("Unknown", bare["site_evidence"]);
        Assert.Equal("No Contact", bare["contact_readiness"]);
        foreach (var col in new[] { "parcel_check_status", "highway_access_status", "parking_verified_status", "parking_verified_count",
                     "truck_entrance_status", "turning_space_status", "power_status" })
            Assert.Equal("Unknown", bare[col]);
        Assert.Equal("", bare["contact_name"]);
        Assert.Equal("Not reviewed", bare["screening_decision"]);
        Assert.NotEqual("", bare["missing_verification"]);

        var good = rows["TEST-0002"];
        Assert.Equal("Verified", good["site_evidence"]);
        Assert.Equal("Relevant Contact Verified", good["contact_readiness"]);
        Assert.Equal("Pat Facilities", good["contact_name"]);
        Assert.Equal("FacilityManager", good["contact_role"]);
        Assert.Equal("Director of Facilities", good["contact_title"]);
        Assert.Equal("Acme Realty", good["contact_organization"]);
        Assert.Equal("555-0100", good["contact_phone"]);
        Assert.Equal("pat@acme.example", good["contact_email"]);
        Assert.Equal("Verified", good["contact_status"]);
        Assert.Equal(Src, good["contact_source_url"]);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", good["contact_last_checked"]);
        Assert.Contains(Src, good["evidence_source_urls"]);
        Assert.Contains("https://tax.example/b749-l1", good["evidence_source_urls"]);
        Assert.Equal("Verified", good["parcel_check_status"]);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", good["parcel_checked_on"]);

        Assert.Equal("Needs more research", rows["TEST-0004"]["screening_decision"]);
        Assert.Equal("Unverified", rows["TEST-0004"]["contact_status"]);
        Assert.Equal("Concern", rows["TEST-0005"]["power_status"]);

        // Same scores and ranks as the ranked list.
        var list = await Json(await client.GetAsync("/api/sites?search=TEST-0002&page=1&pageSize=5"));
        var item = list.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("siteId").GetString() == "TEST-0002");
        Assert.Equal(item.GetProperty("rank").GetInt32().ToString(), good["rank"]);
        Assert.Equal(item.GetProperty("score").GetDouble(), double.Parse(good["c3_score"], System.Globalization.CultureInfo.InvariantCulture), 2);

        // Filters apply to the research export too.
        var filtered = ParseCsv(Encoding.UTF8.GetString(await client.GetByteArrayAsync("/api/sites/export?format=research&contactStates=relevant")).TrimStart('﻿'));
        Assert.Equal(2, filtered.Count);
        Assert.Equal("TEST-0002", filtered[1][0]);
    }

    [Fact]
    public async Task Research_records_never_change_scores_or_rankings()
    {
        var client = Client();
        static async Task<string> Snapshot(HttpClient c)
        {
            var p = await Json(await c.GetAsync("/api/sites?sortBy=Score&descending=true&page=1&pageSize=500"));
            return string.Join(";", p.GetProperty("items").EnumerateArray().Select(i =>
                $"{i.GetProperty("siteId").GetString()}:{i.GetProperty("score").GetDouble()}:{i.GetProperty("rank").GetInt32()}"));
        }
        var before = await Snapshot(client);
        await SeedResearchStates(client);
        Assert.Equal(before, await Snapshot(client));
    }

    [Fact]
    public async Task Research_filters_page_stably_without_repeats_or_gaps()
    {
        var client = Client();
        await SeedResearchStates(client);
        foreach (var q in new[] { "evidenceStates=unknown&contactStates=none", "evidenceStates=preliminary&evidenceStates=mismatch&evidenceStates=verified" })
        {
            var seen = new List<string>();
            int total, pages, page = 1;
            do
            {
                var body = await Json(await client.GetAsync($"/api/sites?{q}&page={page}&pageSize=3&sortBy=OpenAcres&descending=false"));
                total = body.GetProperty("totalCount").GetInt32();
                pages = body.GetProperty("totalPages").GetInt32();
                seen.AddRange(body.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("siteId").GetString()!));
            } while (++page <= pages);
            Assert.True(total > 0);
            Assert.Equal(total, seen.Count);
            Assert.Equal(total, seen.Distinct().Count());
        }
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("   ")]
    public async Task A_verified_contact_needs_a_well_formed_source(string source)
    {
        var r = await Client().PostAsJsonAsync("/api/contacts", new
        {
            siteId = "TEST-0002", name = "Owner", contactType = "PropertyOwner", verificationStatus = "Verified", sourceUrl = source
        });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Table_site_details_and_csv_report_the_same_states()
    {
        var client = Client();
        await SeedResearchStates(client);
        var csv = ParseCsv(Encoding.UTF8.GetString(await client.GetByteArrayAsync("/api/sites/export?format=research&search=TEST-000")).TrimStart('﻿'));
        var h = csv[0];
        var csvRows = csv.Skip(1).ToDictionary(r => r[0], r => (Ev: r[Array.IndexOf(h, "site_evidence")], Ct: r[Array.IndexOf(h, "contact_readiness")]));
        var csvLabel = new Dictionary<string, string>
        {
            ["RelevantContactVerified"] = "Relevant Contact Verified", ["GeneralBusinessContact"] = "General Business Contact",
            ["UnverifiedLead"] = "Unverified Lead", ["NoContact"] = "No Contact"
        };
        foreach (var siteId in new[] { "TEST-0002", "TEST-0003", "TEST-0004", "TEST-0005", "TEST-0006" })
        {
            var row = (await Json(await client.GetAsync($"/api/sites?search={siteId}&page=1&pageSize=5"))).GetProperty("items")
                .EnumerateArray().Single(i => i.GetProperty("siteId").GetString() == siteId);
            var detail = await Json(await client.GetAsync($"/api/sites/{row.GetProperty("id").GetInt32()}"));
            var ev = row.GetProperty("siteEvidence").GetString()!;
            var ct = row.GetProperty("contactReadiness").GetString()!;
            Assert.Equal(ev, detail.GetProperty("summary").GetProperty("siteEvidence").GetString());
            Assert.Equal(ev, detail.GetProperty("validation").GetProperty("siteEvidence").GetString());
            Assert.Equal(ct, detail.GetProperty("validation").GetProperty("contactReadiness").GetString());
            Assert.Equal(ev, csvRows[siteId].Ev);
            Assert.Equal(csvLabel[ct], csvRows[siteId].Ct);
            // The single-state filter returns this site.
            var token = ev.ToLowerInvariant();
            Assert.Contains((await Json(await client.GetAsync($"/api/sites?evidenceStates={token}&search={siteId}&page=1&pageSize=5"))).GetProperty("items").EnumerateArray(),
                i => i.GetProperty("siteId").GetString() == siteId);
        }
    }

    [Fact]
    public async Task Table_shows_the_most_relevant_contact_and_matches_the_research_csv()
    {
        var client = Client();
        await SeedResearchStates(client);
        // A second, more relevant verified contact on TEST-0002: a property owner outranks a facility manager.
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/contacts", new
        {
            siteId = "TEST-0002", name = "Olive Owner", contactType = "PropertyOwner", verificationStatus = "Verified",
            sourceUrl = Src, jobTitle = "Managing Member", organization = "Owner LLC", email = "olive@owner.example"
        })).StatusCode);

        async Task<JsonElement> Row(string siteId) =>
            (await Json(await client.GetAsync($"/api/sites?search={siteId}&page=1&pageSize=5"))).GetProperty("items")
                .EnumerateArray().Single(i => i.GetProperty("siteId").GetString() == siteId);

        var top = (await Row("TEST-0002")).GetProperty("topContact");
        Assert.Equal("Olive Owner", top.GetProperty("name").GetString());
        Assert.Equal("PropertyOwner", top.GetProperty("role").GetString());
        Assert.True(top.GetProperty("verified").GetBoolean());
        Assert.Equal("olive@owner.example", top.GetProperty("email").GetString());
        Assert.Equal(JsonValueKind.Null, top.GetProperty("phone").ValueKind); // not recorded -> not invented
        Assert.Equal(1, top.GetProperty("otherContacts").GetInt32());

        var lead = (await Row("TEST-0004")).GetProperty("topContact");
        Assert.False(lead.GetProperty("verified").GetBoolean());
        Assert.Equal("555-0100", lead.GetProperty("phone").GetString());
        Assert.False((await Row("TEST-0003")).GetProperty("topContact").GetProperty("role").GetString() is "PropertyOwner" or "Broker");
        Assert.Equal(JsonValueKind.Null, (await Row("TEST-0006")).GetProperty("topContact").ValueKind);

        // Same contact in the research CSV, and in Site Details.
        var csv = ParseCsv(Encoding.UTF8.GetString(await client.GetByteArrayAsync("/api/sites/export?format=research&search=TEST-000")).TrimStart('﻿'));
        var h = csv[0];
        string Cell(string siteId, string col) => csv.Skip(1).Single(r => r[0] == siteId)[Array.IndexOf(h, col)];
        foreach (var siteId in new[] { "TEST-0002", "TEST-0003", "TEST-0004", "TEST-0006" })
        {
            var row = await Row(siteId);
            var tc = row.GetProperty("topContact");
            Assert.Equal(tc.ValueKind == JsonValueKind.Null ? "" : tc.GetProperty("name").GetString(), Cell(siteId, "contact_name"));
            Assert.Equal(tc.ValueKind == JsonValueKind.Null ? "" : tc.GetProperty("verified").GetBoolean() ? "Verified" : "Unverified", Cell(siteId, "contact_status"));
            var detail = await Json(await client.GetAsync($"/api/sites/{row.GetProperty("id").GetInt32()}"));
            Assert.Equal(tc.ToString(), detail.GetProperty("summary").GetProperty("topContact").ToString());
        }
    }

    [Fact]
    public async Task Listed_business_phone_is_shown_as_source_data_and_never_counts_as_a_contact()
    {
        var client = Client();
        await _factory.SetBusinessPhoneAsync("TEST-0007", "+19735550100");
        try
        {
            async Task<JsonElement> Row(string siteId) =>
                (await Json(await client.GetAsync($"/api/sites?search={siteId}&page=1&pageSize=5"))).GetProperty("items")
                    .EnumerateArray().Single(i => i.GetProperty("siteId").GetString() == siteId);
            var row = await Row("TEST-0007");
            Assert.Equal("+19735550100", row.GetProperty("businessPhone").GetString());
            Assert.Equal("NoContact", row.GetProperty("contactReadiness").GetString()); // a listed line is not a contact
            Assert.Equal(JsonValueKind.Null, row.GetProperty("topContact").ValueKind);
            Assert.Equal(JsonValueKind.Null, (await Row("TEST-0008")).GetProperty("businessPhone").ValueKind); // nothing invented
            Assert.Equal(1, await TotalFor(client, "contactStates=none&search=TEST-0007"));

            var csv = ParseCsv(Encoding.UTF8.GetString(await client.GetByteArrayAsync("/api/sites/export?format=research&search=TEST-000")).TrimStart('﻿'));
            var col = Array.IndexOf(csv[0], "business_phone_listed_in_source");
            Assert.Equal("+19735550100", csv.Skip(1).Single(r => r[0] == "TEST-0007")[col]);
            Assert.Equal("", csv.Skip(1).Single(r => r[0] == "TEST-0008")[col]);
        }
        finally { await _factory.SetBusinessPhoneAsync("TEST-0007", null); }
    }

    [Fact]
    public async Task Table_row_shows_the_same_imported_listing_as_site_details_and_csv()
    {
        var client = Client();
        await _factory.SetListingAsync("TEST-0009", "IMC powered by Parts Authority; Powpack", "8008748925", "111 Littleton Rd Ste 100", "Parsippany, Nj", "07054");
        try
        {
            var row = (await Json(await client.GetAsync("/api/sites?search=TEST-0009&page=1&pageSize=5"))).GetProperty("items")
                .EnumerateArray().Single(i => i.GetProperty("siteId").GetString() == "TEST-0009");
            var detail = await Json(await client.GetAsync($"/api/sites/{row.GetProperty("id").GetInt32()}"));
            Assert.Equal(detail.GetProperty("summary").GetProperty("businesses").GetString(), row.GetProperty("businesses").GetString());
            Assert.Equal(detail.GetProperty("businessPhone").GetString(), row.GetProperty("businessPhone").GetString());
            Assert.Equal(detail.GetProperty("ownerMailAddress").GetString(), row.GetProperty("ownerMailAddress").GetString());
            Assert.Equal(detail.GetProperty("ownerMailCity").GetString(), row.GetProperty("ownerMailCity").GetString());
            Assert.Equal(detail.GetProperty("ownerMailZip").GetString(), row.GetProperty("ownerMailZip").GetString());
            Assert.False(row.GetProperty("businessListingConfirmed").GetBoolean());
            Assert.Equal("NoContact", row.GetProperty("contactReadiness").GetString()); // an imported listing is not a contact

            var csv = ParseCsv(Encoding.UTF8.GetString(await client.GetByteArrayAsync("/api/sites/export?format=research&search=TEST-0009")).TrimStart('﻿'));
            var r = csv.Skip(1).Single(x => x[0] == "TEST-0009");
            Assert.Equal("111 Littleton Rd Ste 100, Parsippany, Nj 07054", r[Array.IndexOf(csv[0], "owner_mailing_address")]);
            Assert.Equal("8008748925", r[Array.IndexOf(csv[0], "business_phone_listed_in_source")]);

            // A reviewer's evidence-backed business-at-address check marks the listing as confirmed (phone stays unverified).
            await client.PutAsJsonAsync("/api/feasibility", new { siteId = "TEST-0009", itemKey = "BusinessAtAddress", status = "Verified", evidence = "Signage matches (Street View 2026-10)" });
            var after = (await Json(await client.GetAsync("/api/sites?search=TEST-0009&page=1&pageSize=5"))).GetProperty("items")
                .EnumerateArray().Single(i => i.GetProperty("siteId").GetString() == "TEST-0009");
            Assert.True(after.GetProperty("businessListingConfirmed").GetBoolean());
        }
        finally { await _factory.SetListingAsync("TEST-0009", null, null, null, null, null); }
    }

    [Fact]
    public async Task Table_row_carries_the_same_business_website_as_site_details()
    {
        var client = Client();
        await _factory.SetListingAsync("TEST-0010", "The UPS Store; UPS Lawnside Hub", "(856) 573-6313", null, null, null);
        await _factory.SetBusinessWebsiteAsync("TEST-0010", "http://www.theupsstore.com");
        try
        {
            var row = (await Json(await client.GetAsync("/api/sites?search=TEST-0010&page=1&pageSize=5"))).GetProperty("items")
                .EnumerateArray().Single(i => i.GetProperty("siteId").GetString() == "TEST-0010");
            var items = (await Json(await client.GetAsync("/api/sites?page=1&pageSize=100"))).GetProperty("items").EnumerateArray().ToList();
            Assert.NotEmpty(items);
            var detail = await Json(await client.GetAsync($"/api/sites/{row.GetProperty("id").GetInt32()}"));
            Assert.Equal("TEST-0010", detail.GetProperty("summary").GetProperty("siteId").GetString());
            Assert.Equal("http://www.theupsstore.com", row.GetProperty("businessWebsite").GetString());
            Assert.Equal(detail.GetProperty("businessWebsite").GetString(), row.GetProperty("businessWebsite").GetString());
            // Other rows don't pick up this site's website.
            Assert.All(items.Where(i => i.GetProperty("siteId").GetString() != "TEST-0010"),
                i => Assert.NotEqual("http://www.theupsstore.com", i.GetProperty("businessWebsite").GetString()));
        }
        finally
        {
            await _factory.SetBusinessWebsiteAsync("TEST-0010", null);
            await _factory.SetListingAsync("TEST-0010", null, null, null, null, null);
        }
    }

    /// <summary>Minimal RFC-4180 reader (quoted fields, doubled quotes, embedded commas/newlines).</summary>
    private static List<string[]> ParseCsv(string text)
    {
        var rows = new List<string[]>();
        var row = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (quoted)
            {
                if (ch == '"' && i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                else if (ch == '"') quoted = false;
                else cell.Append(ch);
            }
            else if (ch == '"') quoted = true;
            else if (ch == ',') { row.Add(cell.ToString()); cell.Clear(); }
            else if (ch == '\n')
            {
                row.Add(cell.ToString().TrimEnd('\r')); cell.Clear();
                rows.Add([.. row]); row.Clear();
            }
            else cell.Append(ch);
        }
        if (cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString()); rows.Add([.. row]); }
        return rows;
    }
}
