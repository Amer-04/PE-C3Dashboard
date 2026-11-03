using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace NJTruckCharging.Tests.Integration;

/// <summary>
/// Hosts the app in a non-Development (Production) environment to prove the destructive admin
/// endpoints are disabled unless the separate admin key is supplied.
/// </summary>
public class ProdWebAppFactory : WebApplicationFactory<Program>
{
    public const string AdminKey = "prod-test-admin-key";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.UseSetting("ConnectionStrings:DefaultConnection",
            @"Server=(localdb)\MSSQLLocalDB;Database=NJTruckCharging_ProdTest;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True");
        builder.UseSetting("Import:WorkbookPath", ""); // no import
        builder.UseSetting("Admin:ApiKey", AdminKey);  // the separate admin mechanism
    }
}

public class ProductionAdminTests(ProdWebAppFactory factory) : IClassFixture<ProdWebAppFactory>
{
    private readonly ProdWebAppFactory _factory = factory;

    [Fact]
    public async Task Admin_rescore_is_forbidden_in_production_without_key()
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsync("/api/admin/rescore", null);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Admin_import_is_forbidden_in_production_without_key()
    {
        var client = _factory.CreateClient();
        using var content = new MultipartFormDataContent { { new ByteArrayContent([1, 2, 3]), "file", "x.xlsx" } };
        var resp = await client.PostAsync("/api/admin/import", content);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Admin_rescore_is_forbidden_with_wrong_key()
    {
        var client = _factory.CreateClient();
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/admin/rescore");
        req.Headers.Add("X-Admin-Key", "wrong");
        var resp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Admin_rescore_is_allowed_with_the_configured_key()
    {
        var client = _factory.CreateClient();
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/admin/rescore");
        req.Headers.Add("X-Admin-Key", ProdWebAppFactory.AdminKey);
        var resp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Normal_endpoints_still_open_in_production()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/methodology");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }
}
