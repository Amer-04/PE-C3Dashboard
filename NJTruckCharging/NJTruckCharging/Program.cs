using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.ResponseCompression;
using NJTruckCharging.Application;
using NJTruckCharging.Application.Sites;
using NJTruckCharging.Infrastructure;
using NJTruckCharging.Security;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(o => o.AddServerHeader = false);   // don't advertise the server software

// Cloud deployment (PostgreSQL / Supabase) is fail-closed: it refuses to start unless sign-in with an allowlist and
// read-only mode are both configured. Local/office deployments (SQL Server) are unaffected.
var authEnabled = SupabaseAuth.IsEnabled(builder.Configuration);
var readOnlyDemo = builder.Configuration.GetValue<bool>("ReadOnlyDemo:Enabled");
if (DatabaseProvider.IsPostgres(builder.Configuration))
{
    if (!authEnabled) throw new InvalidOperationException("The cloud (PostgreSQL) deployment requires Auth:Mode=Supabase.");
    if (!readOnlyDemo) throw new InvalidOperationException("The cloud (PostgreSQL) deployment requires ReadOnlyDemo:Enabled=true.");
    // Access rule, allowlist/blocklist and the publishable key must form a working, non-empty configuration.
    SupabaseAuth.ValidateAccessConfiguration(builder.Configuration);
}
// Shared methodology editing: on PostgreSQL it needs sign-in and its own restricted writer login (fail-closed).
MethodologyEditing.ValidateConfiguration(builder.Configuration);
var methodologyCloudEditing = MethodologyEditing.IsCloudEditingEnabled(builder.Configuration);
if (authEnabled) builder.Services.AddSupabaseAuth(builder.Configuration);

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
    .WithHeaders("Content-Type", "Accept", "Authorization")
    .WithMethods("GET", "POST", "PUT", "DELETE")
    .WithExposedHeaders("Content-Disposition", "X-Methodology-Version")));

// Behind a TLS-terminating host (Render): honour only X-Forwarded-Proto so HTTPS is recognised. Client IPs from
// X-Forwarded-For are deliberately NOT trusted.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});
builder.Services.AddHsts(o => { o.MaxAge = TimeSpan.FromDays(365); o.IncludeSubDomains = true; });

// Rate limits for methodology changes, per signed-in user (Supabase user id). A recalculation (Review Results, Apply
// Scoring, Restore) scores every site, so it is limited tightly; ordinary draft edits generously. Configurable:
// Methodology:RateLimits:ScoringPerWindow / ScoringWindowSeconds / EditsPerMinute.
builder.Services.AddRateLimiter(o =>
{
    var limits = builder.Configuration.GetSection("Methodology:RateLimits");
    var scoringPermits = limits.GetValue("ScoringPerWindow", 6);
    var scoringWindow = TimeSpan.FromSeconds(limits.GetValue("ScoringWindowSeconds", 600));
    var editPermits = limits.GetValue("EditsPerMinute", 120);
    o.AddPolicy(MethodologyEditing.ScoringRateLimitPolicy, ctx => RateLimitPartition.GetFixedWindowLimiter(
        MethodologyEditing.Partition(ctx), _ => new FixedWindowRateLimiterOptions { PermitLimit = scoringPermits, Window = scoringWindow, QueueLimit = 0 }));
    o.AddPolicy(MethodologyEditing.EditRateLimitPolicy, ctx => RateLimitPartition.GetFixedWindowLimiter(
        MethodologyEditing.Partition(ctx), _ => new FixedWindowRateLimiterOptions { PermitLimit = editPermits, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    o.OnRejected = async (ctx, ct) =>
    {
        var retry = ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var after) ? (int)Math.Ceiling(after.TotalSeconds) : 60;
        ctx.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        ctx.HttpContext.Response.Headers.RetryAfter = retry.ToString();
        await ctx.HttpContext.Response.WriteAsJsonAsync(new Microsoft.AspNetCore.Mvc.ProblemDetails
        {
            Status = 429,
            Title = $"Too many scoring requests. Please wait about {Math.Max(1, (retry + 59) / 60)} minute(s) and try again.",
            Extensions = { ["code"] = "rate_limited" }
        }, options: null, contentType: "application/problem+json", cancellationToken: ct);
    };
});

var app = builder.Build();

if (builder.Configuration.GetValue<bool>("Hosting:BehindProxy")) app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseResponseCompression();

if (app.Environment.IsDevelopment() && !authEnabled)
{
    app.MapOpenApi();
}
else if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    // The health check is answered over plain HTTP for the hosting platform's probe.
    app.UseWhen(ctx => !ctx.Request.Path.StartsWithSegments("/healthz"), a => a.UseHttpsRedirection());
}

app.Use((ctx, next) =>
{
    var h = ctx.Response.Headers;
    h.XContentTypeOptions = "nosniff";
    h.XFrameOptions = "DENY";
    h["Referrer-Policy"] = "no-referrer";
    if (authEnabled) h.CacheControl = "no-store";       // signed-in data must not sit in shared caches
    return next(ctx);
});

app.UseCors(AngularCors);

if (authEnabled)
{
    app.UseAuthentication();
    app.UseAuthorization();
}

app.UseRateLimiter();

// Read-only demo: every request that could change data is refused here, after sign-in is checked (so an anonymous
// caller still gets 401) and before any controller runs — whatever the endpoint (bookmarks, contacts, research,
// screening, feasibility, custom fields, admin import/rescore). The ONLY exception: with
// Methodology:CloudEditing:Enabled=true, the shared methodology editor endpoints (marked RequireMethodologyEditor,
// matched by the routed endpoint's metadata, not by URL text), which write through the restricted writer login.
if (readOnlyDemo)
{
    app.Use(async (ctx, next) =>
    {
        if (HttpMethods.IsGet(ctx.Request.Method) || HttpMethods.IsHead(ctx.Request.Method) || HttpMethods.IsOptions(ctx.Request.Method)
            || MethodologyEditing.AllowedInReadOnlyMode(ctx, methodologyCloudEditing))
        {
            await next(ctx);
            return;
        }
        ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
        await ctx.Response.WriteAsJsonAsync(new Microsoft.AspNetCore.Mvc.ProblemDetails
        {
            Status = 403,
            Title = "This online demo is read-only. Changes are not saved.",
            Extensions = { ["code"] = "read_only" }
        }, options: null, contentType: "application/problem+json");
    });
}

app.MapGet("/healthz", () => Results.Text("ok")).AllowAnonymous();

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
