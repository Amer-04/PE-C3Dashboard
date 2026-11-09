using System.Net.Http.Json;
using System.Text.Json;

namespace NJTruckCharging.Tests.Integration;

/// <summary>Site Details "at a glance" summary through the real API (same fixture/class as the other integration tests).</summary>
public partial class ApiIntegrationTests
{
    private static JsonElement Answer(JsonElement detail, string key) =>
        detail.GetProperty("suitability").GetProperty("items").EnumerateArray().Single(i => i.GetProperty("key").GetString() == key);

    [Fact]
    public async Task Site_detail_summary_starts_unverified_and_follows_the_teams_research()
    {
        var client = Client();
        const string siteId = "TEST-0007"; // 4B, 3 open ac, no freight
        var id = await CandidateIdOf(client, siteId);

        var before = await Json(await client.GetAsync($"/api/sites/{id}"));
        var s = before.GetProperty("suitability");
        Assert.Equal(8, s.GetProperty("items").GetArrayLength());
        Assert.Equal(0, s.GetProperty("verifiedAnswers").GetInt32());
        Assert.Equal("Estimate", Answer(before, "Capacity").GetProperty("basis").GetString());
        foreach (var k in new[] { "TruckSpaces", "Access", "Power", "Contact" })
            Assert.Equal("Unknown", Answer(before, k).GetProperty("answer").GetString());
        Assert.True(s.GetProperty("remainingToVerify").GetArrayLength() >= 5);

        // Team research arrives.
        await client.PutAsJsonAsync("/api/feasibility", new { siteId, itemKey = "TruckParkingCapacity", status = "Verified", sourceUrl = "https://example.org/evidence", evidenceDate = "2026-10-01", evidence = "32 tractor-trailer stalls striped in the rear yard" });
        await client.PutAsJsonAsync("/api/feasibility", new { siteId, itemKey = "TruckEntranceAccess", status = "Verified", sourceUrl = "https://example.org/evidence", evidenceDate = "2026-10-01", evidence = "Two 40-ft curb cuts" });
        await client.PutAsJsonAsync("/api/feasibility", new { siteId, itemKey = "ElectricalService", status = "Concern", evidence = "Utility: 18-month upgrade" });
        await client.PostAsJsonAsync("/api/contacts", new { siteId, name = "Jo Owner", contactType = "PropertyOwner", verificationStatus = "Verified", sourceUrl = "https://example.org/jo" });

        var after = await Json(await client.GetAsync($"/api/sites/{id}"));
        var spaces = Answer(after, "TruckSpaces");
        Assert.Equal("Yes", spaces.GetProperty("answer").GetString());
        Assert.Equal("Verified", spaces.GetProperty("basis").GetString());
        Assert.Contains("32", spaces.GetProperty("headline").GetString());
        Assert.Equal("NeedsCheck", Answer(after, "Access").GetProperty("answer").GetString()); // turning space not checked yet
        Assert.Equal("No", Answer(after, "Power").GetProperty("answer").GetString());
        Assert.Contains("Jo Owner", Answer(after, "Contact").GetProperty("headline").GetString());
        Assert.Equal(5, after.GetProperty("suitability").GetProperty("verifiedAnswers").GetInt32()); // capacity, truck spaces, access (entrance part), power, contact

        // Informational only: score and rank are untouched.
        Assert.Equal(before.GetProperty("summary").GetProperty("score").GetDouble(), after.GetProperty("summary").GetProperty("score").GetDouble());
        Assert.Equal(before.GetProperty("summary").GetProperty("rank").GetInt32(), after.GetProperty("summary").GetProperty("rank").GetInt32());
    }
}
