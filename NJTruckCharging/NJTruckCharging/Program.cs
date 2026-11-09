using System.Text.Json.Serialization;
using Microsoft.AspNetCore.ResponseCompression;
using NJTruckCharging.Application;
using NJTruckCharging.Application.Sites;
using NJTruckCharging.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

// The map endpoint returns ~30k points; compress responses.
builder.Services.AddResponseCompression(o =>
{
    o.EnableForHttps = true;
    o.Providers.Add<BrotliCompressionProvider>();
    o.Providers.Add<GzipCompressionProvider>();
});

// CORS is restricted to the configured dashboard origin(s) and the HTTP verbs the API actually uses.
const string AngularCors = "angular";
builder.Services.AddCors(o => o.AddPolicy(AngularCors, p => p
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:4200"])
    .WithHeaders("Content-Type", "Accept")
    .WithMethods("GET", "POST", "PUT", "DELETE")
    .WithExposedHeaders("Content-Disposition", "X-Methodology-Version")));

var app = builder.Build();

app.UseExceptionHandler();
app.UseResponseCompression();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
else
{
    app.UseHttpsRedirection();
}

app.UseCors(AngularCors);

// Every API response states which methodology version produced the scores it contains, so the dashboard can detect
// a publish made elsewhere and reload instead of mixing score versions.
var activeMethodology = app.Services.GetRequiredService<NJTruckCharging.Application.Methodology.ActiveMethodologyCache>();
app.Use((ctx, next) =>
{
    ctx.Response.OnStarting(() =>
    {
        if (activeMethodology.Current is { } a) ctx.Response.Headers["X-Methodology-Version"] = a.VersionId.ToString();
        return Task.CompletedTask;
    });
    return next(ctx);
});

app.MapControllers();

// Create / migrate the database and seed it from the workbook on first run.
await app.Services.InitializeDatabaseAsync();

// Warm up EF model + query plans so the first dashboard load isn't a multi-second cold compile.
using (var warmupScope = app.Services.CreateScope())
{
    var sites = warmupScope.ServiceProvider.GetRequiredService<NJTruckCharging.Application.Sites.SiteQueryService>();
    try
    {
        await sites.GetPageAsync(new SiteFilter(), new SiteListQuery());
        await sites.GetMapPointsAsync(new SiteFilter());
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Query warmup failed (non-fatal).");
    }
}

app.Run();

// Exposed so the integration test project can host the real app with WebApplicationFactory<Program>.
public partial class Program;
