using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NJTruckCharging.Domain.Entities;
using NJTruckCharging.Domain.Scoring;
using NJTruckCharging.Infrastructure.Persistence;

namespace NJTruckCharging.Tests.Integration;

/// <summary>
/// Hosts the real application against a dedicated LocalDB test database (the actual provider), with the
/// workbook import skipped. Seeds a few hundred candidate sites. The app has no authentication.
/// </summary>
public class TestWebAppFactory : WebApplicationFactory<Program>
{
    public const int SeededSiteCount = 1500;

    /// <summary>Each factory type gets its own LocalDB database so test classes can run in parallel.</summary>
    protected virtual string DatabaseName => "NJTruckCharging_IntegrationTest";

    public string TestConnection =>
        $@"Server=(localdb)\MSSQLLocalDB;Database={DatabaseName};Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True";

    /// <summary>Lets a test make the activation transaction fail right before commit.</summary>
    public FaultInjectionHook Faults { get; } = new();

    private readonly SemaphoreSlim _seedLock = new(1, 1);
    private bool _seeded;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:DefaultConnection", TestConnection);
        builder.UseSetting("Import:WorkbookPath", "");        // skip the 30k-row workbook import
        builder.ConfigureServices(services =>
        {
            // TestServer has no TCP peer: present requests as coming from loopback (the trusted dashboard host) unless a
            // test sets X-Test-Remote-IP to simulate a client on another network.
            services.AddSingleton<Microsoft.AspNetCore.Hosting.IStartupFilter, TestRemoteIpStartupFilter>();
            services.RemoveAll<NJTruckCharging.Application.Abstractions.IActivationHook>();
            services.AddSingleton<NJTruckCharging.Application.Abstractions.IActivationHook>(Faults);
        });
    }

    /// <summary>A client on the trusted internal network (loopback) — no credential involved.</summary>
    public HttpClient CreateTrustedClient() => CreateClient();

    /// <summary>A client whose requests appear to come from <paramref name="ip"/>.</summary>
    public HttpClient CreateClientFrom(string ip)
    {
        var c = CreateClient();
        c.DefaultRequestHeaders.Add(TestRemoteIpStartupFilter.Header, ip);
        return c;
    }

    /// <summary>Migrates the test DB and seeds candidate sites. Idempotent.</summary>
    public async Task EnsureSeededAsync()
    {
        if (_seeded) return;
        await _seedLock.WaitAsync();
        try
        {
            if (_seeded) return;
            using var _ = CreateClient(); // forces host build → migrate + warmup
            using var scope = Services.CreateScope();
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<AppDbContext>();

            if (!await db.CandidateSites.AnyAsync())
            {
                var sites = BuildSites(SeededSiteCount);
                SiteScorer.ScoreAll(sites);
                db.CandidateSites.AddRange(sites);
                await db.SaveChangesAsync();
                // Score the seeded sites with the active methodology (stores Version-1 scores + materialises).
                var m = sp.GetRequiredService<NJTruckCharging.Application.Methodology.MethodologyService>();
                var r = await m.ReapplyActiveAsync();
                if (r.Error != NJTruckCharging.Application.Methodology.MethodologyError.None)
                    throw new InvalidOperationException("Seeding: " + r.Message);
            }
            _seeded = true;
        }
        finally { _seedLock.Release(); }
    }

    public async Task ClearSavedAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync("DELETE FROM SavedSites;");
    }

    public async Task ClearContactsAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync("DELETE FROM SiteContacts;");
    }

    public async Task ClearFeasibilityAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync("DELETE FROM FeasibilityItems;");
    }

    public async Task ClearScreeningAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync("DELETE FROM SiteScreeningEntries; DELETE FROM SiteScreenings;");
    }

    /// <summary>
    /// Simulates a workbook re-import the way WriteStore.ReplaceAllAsync does it: every CandidateSite row is deleted
    /// and re-inserted, so identity ids are regenerated while SiteIds stay the same.
    /// </summary>
    public async Task SimulateReimportAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sites = await db.CandidateSites.AsNoTracking().ToListAsync();
        await db.NearbyFleets.ExecuteDeleteAsync();
        await db.CandidateSites.ExecuteDeleteAsync();
        foreach (var s in sites) s.Id = 0;
        db.CandidateSites.AddRange(sites);
        await db.SaveChangesAsync();
    }

    /// <summary>Sets (or clears) the source-data business phone of one seeded candidate.</summary>
    public async Task SetBusinessPhoneAsync(string siteId, string? phone)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE CandidateSites SET BusinessPhone = {phone} WHERE SiteId = {siteId}");
    }

    /// <summary>Sets the imported business listing / owner mailing fields of one seeded candidate (null clears).</summary>
    public async Task SetListingAsync(string siteId, string? businesses, string? phone, string? addr, string? city, string? zip)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE CandidateSites SET BusinessesOnSite = {businesses}, BusinessPhone = {phone}, OwnerMailAddress = {addr}, OwnerMailCity = {city}, OwnerMailZip = {zip} WHERE SiteId = {siteId}");
    }

    public async Task SetBusinessWebsiteAsync(string siteId, string? website)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE CandidateSites SET BusinessWebsite = {website} WHERE SiteId = {siteId}");
    }

    /// <summary>A sample candidate SiteId that exists in the seeded data.</summary>
    public string SampleSiteId => "TEST-0001";

    private static List<CandidateSite> BuildSites(int count)
    {
        var list = new List<CandidateSite>(count);
        for (var i = 0; i < count; i++)
        {
            var seg = (i % 6) + 1;
            list.Add(new CandidateSite
            {
                SiteId = $"TEST-{i + 1:0000}",
                PamsPin = $"PIN-{i}",
                Segment = seg,
                SiteAddress = $"{i + 1} Industrial Way",
                Municipality = "Testville",
                County = "Union",
                PropClass = (i % 3) switch { 0 => "4B", 1 => "1", _ => "4A" },
                OpenAcresEst = (i % 10) * 0.5,          // 0 (Unknown) .. 4.5
                FreightUse = i % 5 == 0,                // some sites have on-site freight use (IOS Confirmed evidence)
                MilesToExit = 0.2 + (i % 20) * 0.25,
                TrucksBased5Mi = i % 50,
                LargeWarehouses2Mi = i % 7,
                TruckAadtAtExit = (i % 100) * 10,
                MilesToTruckRoad = 0.1,
                MilesToTruckDcfc = 15,
                MilesToAnyDcfc = 5,
                Compactness = 0.6,
                ShortSideFt = 200,
                FloodZone100Yr = "none",
                Latitude = 40.6 + i * 0.0001,
                Longitude = -74.2 - i * 0.0001,
            });
        }
        foreach (var s in list) s.RefreshDerivedFlags();
        return list;
    }
}

/// <summary>Throws inside the activation transaction (after the live columns were updated) when armed.</summary>
public sealed class FaultInjectionHook : NJTruckCharging.Application.Abstractions.IActivationHook
{
    public volatile bool FailNextActivation;

    public Task BeforeCommitAsync(int versionId, CancellationToken ct)
    {
        if (!FailNextActivation) return Task.CompletedTask;
        FailNextActivation = false;
        throw new InvalidOperationException("Injected failure after materialising scores (test).");
    }
}

/// <summary>Test-only: sets Connection.RemoteIpAddress (null under TestServer) from X-Test-Remote-IP, default 127.0.0.1.</summary>
public sealed class TestRemoteIpStartupFilter : Microsoft.AspNetCore.Hosting.IStartupFilter
{
    public const string Header = "X-Test-Remote-IP";

    public Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> Configure(Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> next) => app =>
    {
        app.Use((ctx, nxt) =>
        {
            var ip = ctx.Request.Headers[Header].ToString();
            ctx.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(string.IsNullOrEmpty(ip) ? "127.0.0.1" : ip);
            return nxt(ctx);
        });
        next(app);
    };
}
