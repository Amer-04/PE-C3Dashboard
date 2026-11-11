using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NJTruckCharging.Security;

namespace NJTruckCharging.Tests.Unit;

public class EmployeeEmailPolicyTests
{
    private static readonly JsonElement Vectors = JsonDocument.Parse(File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "employee-email.vectors.json"))).RootElement;

    public static TheoryData<string> Allowed() => [.. Vectors.GetProperty("allowed").EnumerateArray().Select(e => e.GetString()!)];
    public static TheoryData<string> Refused() => [.. Vectors.GetProperty("refused").EnumerateArray().Select(e => e.GetString()!)];

    [Theory]
    [MemberData(nameof(Allowed))]
    public void Shared_vectors_allowed(string email) => Assert.True(EmployeeEmailPolicy.IsAllowed(email), email);

    [Theory]
    [MemberData(nameof(Refused))]
    public void Shared_vectors_refused(string email) => Assert.False(EmployeeEmailPolicy.IsAllowed(email), email);

    [Fact]
    public void The_vectors_include_real_homoglyphs_and_the_domains_are_exactly_the_two_company_domains()
    {
        var refused = Vectors.GetProperty("refused").EnumerateArray().Select(e => e.GetString()!).ToList();
        Assert.Contains(refused, e => e.Any(ch => ch > '~'));
        Assert.Equal(["poweredison.com", "ev-edison.com"], EmployeeEmailPolicy.CompanyDomains);
        Assert.False(EmployeeEmailPolicy.IsAllowed(null));
        Assert.False(EmployeeEmailPolicy.IsAllowed(new string('a', 250) + "@poweredison.com"));    // > 254 characters
    }
}

public class SupabaseUserDirectoryTests
{
    private const string Body = """{"id":"6f1d3c52-7a0e-4b8e-9d2a-1c5b8e7f9a01","email":"pat@poweredison.com","email_confirmed_at":"2026-11-01T10:00:00Z","is_anonymous":false}""";

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(answer(request));
        }
    }

    private sealed class Factory(HttpMessageHandler h) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(h, disposeHandler: false);
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static (SupabaseUserDirectory dir, Handler handler, Clock clock) Make(Func<HttpRequestMessage, HttpResponseMessage> answer, int ttl = 60)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:Supabase:Url"] = "https://proj.supabase.co",
            ["Auth:Supabase:PublishableKey"] = "sb_publishable_test",
            ["Auth:Supabase:UserCheckSeconds"] = ttl.ToString(),
        }).Build();
        var handler = new Handler(answer);
        var clock = new Clock();
        return (new SupabaseUserDirectory(new Factory(handler), config, clock, NullLogger<SupabaseUserDirectory>.Instance), handler, clock);
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Asks_the_project_user_endpoint_with_the_users_token_and_the_publishable_key()
    {
        var (dir, h, _) = Make(_ => Json(Body));
        var u = await dir.LookupAsync("tok-abc", default);
        var req = Assert.Single(h.Requests);
        Assert.Equal("https://proj.supabase.co/auth/v1/user", req.RequestUri!.ToString());
        Assert.Equal("Bearer tok-abc", req.Headers.Authorization!.ToString());
        Assert.Equal("sb_publishable_test", req.Headers.GetValues("apikey").Single());
        Assert.Equal(SupabaseUserStatus.Active, u.Status);
        Assert.Equal("pat@poweredison.com", u.Email);
        Assert.True(u.EmailConfirmed);
        Assert.False(u.Banned);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, SupabaseUserStatus.Revoked)]
    [InlineData(HttpStatusCode.Forbidden, SupabaseUserStatus.Revoked)]
    [InlineData(HttpStatusCode.NotFound, SupabaseUserStatus.Revoked)]
    [InlineData(HttpStatusCode.TooManyRequests, SupabaseUserStatus.Unavailable)]
    [InlineData(HttpStatusCode.InternalServerError, SupabaseUserStatus.Unavailable)]
    [InlineData(HttpStatusCode.BadGateway, SupabaseUserStatus.Unavailable)]
    public async Task Maps_Supabase_refusals_to_revoked_and_outages_to_unavailable(HttpStatusCode code, SupabaseUserStatus expected)
    {
        var (dir, _, _) = Make(_ => Json("{}", code));
        Assert.Equal(expected, (await dir.LookupAsync("t", default)).Status);
    }

    [Fact]
    public async Task Network_errors_and_garbage_never_grant_access()
    {
        var (down, _, _) = Make(_ => throw new HttpRequestException("no route"));
        Assert.Equal(SupabaseUserStatus.Unavailable, (await down.LookupAsync("t", default)).Status);
        var (garbage, _, _) = Make(_ => Json("not json"));
        Assert.Equal(SupabaseUserStatus.Unavailable, (await garbage.LookupAsync("t", default)).Status);
        var (noId, _, _) = Make(_ => Json("""{"email":"pat@poweredison.com","email_confirmed_at":"2026-01-01T00:00:00Z"}"""));
        Assert.Equal(SupabaseUserStatus.Revoked, (await noId.LookupAsync("t", default)).Status);
    }

    [Theory]
    [InlineData("""{"id":"6f1d3c52-7a0e-4b8e-9d2a-1c5b8e7f9a01","email":"p@poweredison.com","email_confirmed_at":null}""", false, false)]
    [InlineData("""{"id":"6f1d3c52-7a0e-4b8e-9d2a-1c5b8e7f9a01","email":"p@poweredison.com"}""", false, false)]
    [InlineData("""{"id":"6f1d3c52-7a0e-4b8e-9d2a-1c5b8e7f9a01","email":"p@poweredison.com","email_confirmed_at":"2026-01-01T00:00:00Z","banned_until":"2126-01-01T00:00:00Z"}""", true, true)]
    [InlineData("""{"id":"6f1d3c52-7a0e-4b8e-9d2a-1c5b8e7f9a01","email":"p@poweredison.com","email_confirmed_at":"2026-01-01T00:00:00Z","banned_until":"2020-01-01T00:00:00Z"}""", true, false)]
    [InlineData("""{"id":"6f1d3c52-7a0e-4b8e-9d2a-1c5b8e7f9a01","email":"p@poweredison.com","email_confirmed_at":"2026-01-01T00:00:00Z","banned_until":"forever?"}""", true, true)]
    public void Reads_confirmation_and_ban_state(string json, bool confirmed, bool banned)
    {
        var u = SupabaseUserDirectory.Parse(JsonDocument.Parse(json).RootElement, new DateTimeOffset(2026, 11, 11, 0, 0, 0, TimeSpan.Zero));
        Assert.Equal(confirmed, u.EmailConfirmed);
        Assert.Equal(banned, u.Banned);
    }

    [Fact]
    public async Task Caches_answers_per_token_for_the_configured_time_but_never_caches_outages()
    {
        var fail = true;
        var (dir, h, clock) = Make(_ => fail ? Json("{}", HttpStatusCode.ServiceUnavailable) : Json(Body), ttl: 60);
        Assert.Equal(SupabaseUserStatus.Unavailable, (await dir.LookupAsync("t", default)).Status);
        fail = false;
        Assert.Equal(SupabaseUserStatus.Active, (await dir.LookupAsync("t", default)).Status);   // outage not cached
        Assert.Equal(2, h.Requests.Count);

        await dir.LookupAsync("t", default);
        Assert.Equal(2, h.Requests.Count);                                                       // cached
        await dir.LookupAsync("other-token", default);
        Assert.Equal(3, h.Requests.Count);                                                       // per token

        clock.Now += TimeSpan.FromSeconds(61);
        await dir.LookupAsync("t", default);
        Assert.Equal(4, h.Requests.Count);                                                       // expired → asked again
    }

    [Fact]
    public async Task Simultaneous_requests_with_the_same_token_share_one_call()
    {
        var gate = new TaskCompletionSource();
        var calls = 0;
        var handler = new GatedHandler(async () => { Interlocked.Increment(ref calls); await gate.Task; return Json(Body); });
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:Supabase:Url"] = "https://proj.supabase.co", ["Auth:Supabase:PublishableKey"] = "k",
        }).Build();
        var dir = new SupabaseUserDirectory(new Factory(handler), config, new Clock(), NullLogger<SupabaseUserDirectory>.Instance);
        var all = Enumerable.Range(0, 8).Select(_ => dir.LookupAsync("same-token", default)).ToList();
        gate.SetResult();
        var answers = await Task.WhenAll(all);
        Assert.Equal(1, calls);
        Assert.All(answers, a => Assert.Equal(SupabaseUserStatus.Active, a.Status));
    }

    private sealed class GatedHandler(Func<Task<HttpResponseMessage>> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => answer();
    }

    [Fact]
    public async Task Cache_time_is_capped_at_five_minutes_and_zero_disables_it()
    {
        var (capped, h1, clock) = Make(_ => Json(Body), ttl: 86400);
        await capped.LookupAsync("t", default);
        clock.Now += TimeSpan.FromSeconds(301);
        await capped.LookupAsync("t", default);
        Assert.Equal(2, h1.Requests.Count);

        var (off, h2, _) = Make(_ => Json(Body), ttl: 0);
        await off.LookupAsync("t", default);
        await off.LookupAsync("t", default);
        Assert.Equal(2, h2.Requests.Count);
    }
}

public class EmployeeAccessHandlerTests
{
    private static readonly Guid Id = Guid.Parse("6f1d3c52-7a0e-4b8e-9d2a-1c5b8e7f9a01");

    private static EmployeeAccessHandler Handler(string rule, string allowed = "", string blocked = "") =>
        new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:AccessRule"] = rule, ["Auth:AllowedUserIds"] = allowed, ["Auth:BlockedUserIds"] = blocked,
        }).Build());

    private static ClaimsPrincipal User(string tokenEmail, params Claim[] serverClaims)
    {
        var token = new ClaimsIdentity([new("sub", Id.ToString()), new("role", "authenticated"), new("email", tokenEmail)], "Bearer");
        var p = new ClaimsPrincipal(token);
        if (serverClaims.Length > 0) p.AddIdentity(new ClaimsIdentity(serverClaims, SupabaseAuth.CheckedClaimsIssuer));
        return p;
    }

    private static Claim Verified(string email, string issuer = SupabaseAuth.CheckedClaimsIssuer) =>
        new(SupabaseAuth.VerifiedEmailClaim, email, ClaimValueTypes.String, issuer);

    [Fact]
    public void A_server_verified_company_email_is_allowed()
    {
        Assert.True(Handler("EmailDomain").IsAllowed(User("pat@poweredison.com", Verified("pat@poweredison.com"))));
        Assert.True(Handler("EmailDomain").IsAllowed(User("PAT@EV-EDISON.COM", Verified("pat@ev-edison.com"))));
    }

    [Fact]
    public void Without_the_server_check_or_with_a_forged_claim_nothing_is_allowed()
    {
        var h = Handler("EmailDomain");
        Assert.False(h.IsAllowed(User("pat@poweredison.com")));                                         // no account check
        Assert.False(h.IsAllowed(User("pat@poweredison.com", Verified("pat@poweredison.com", issuer: "https://proj.supabase.co/auth/v1"))));
        // A claim of the same name smuggled into the token itself carries the token's issuer, not ours.
        var token = new ClaimsIdentity([new("sub", Id.ToString()), new("role", "authenticated"), new("email", "pat@poweredison.com"),
            new(SupabaseAuth.VerifiedEmailClaim, "pat@poweredison.com", ClaimValueTypes.String, "https://proj.supabase.co/auth/v1")], "Bearer");
        Assert.False(h.IsAllowed(new ClaimsPrincipal(token)));
        // Two verified claims (one forged, one real) are refused outright.
        var two = User("pat@poweredison.com", Verified("pat@poweredison.com"));
        two.AddIdentity(new ClaimsIdentity([Verified("x@gmail.com")], "other"));
        Assert.False(h.IsAllowed(two));
    }

    [Fact]
    public void Token_and_verified_email_must_agree_and_the_verified_one_decides()
    {
        var h = Handler("EmailDomain");
        Assert.False(h.IsAllowed(User("pat@poweredison.com", Verified("pat@gmail.com"))));
        Assert.False(h.IsAllowed(User("pat@gmail.com", Verified("pat@poweredison.com"))));
        Assert.False(h.IsAllowed(User("other@poweredison.com", Verified("pat@poweredison.com"))));
    }

    [Fact]
    public void Blocklist_wins_over_every_rule()
    {
        var u = User("pat@poweredison.com", Verified("pat@poweredison.com"));
        Assert.False(Handler("EmailDomain", blocked: Id.ToString()).IsAllowed(u));
        Assert.False(Handler("UserIds", allowed: Id.ToString(), blocked: Id.ToString()).IsAllowed(u));
    }

    [Fact]
    public void Rules_combine_as_documented()
    {
        var company = User("pat@poweredison.com", Verified("pat@poweredison.com"));
        var personal = User("pat@gmail.com", Verified("pat@gmail.com"));
        Assert.True(Handler("UserIds", allowed: Id.ToString()).IsAllowed(personal));            // original behaviour
        Assert.False(Handler("UserIds", allowed: Guid.NewGuid().ToString()).IsAllowed(company));
        Assert.False(Handler("EmailDomainAndUserIds", allowed: Id.ToString()).IsAllowed(personal));
        Assert.True(Handler("EmailDomainAndUserIds", allowed: Id.ToString()).IsAllowed(company));
        Assert.False(Handler("EmailDomainAndUserIds", allowed: Guid.NewGuid().ToString()).IsAllowed(company));
        Assert.False(Handler("EmailDomain").IsAllowed(personal));
    }
}
