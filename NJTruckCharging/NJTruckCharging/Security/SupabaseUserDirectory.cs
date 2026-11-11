using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NJTruckCharging.Security;

public enum SupabaseUserStatus
{
    /// <summary>Supabase confirms the session and returns the account.</summary>
    Active,
    /// <summary>Supabase refuses the token: user deleted, session revoked (signed out / password changed), or invalid.</summary>
    Revoked,
    /// <summary>Supabase could not be asked (network error, timeout, 5xx, rate limit). Never treated as access.</summary>
    Unavailable,
}

/// <summary>The account as Supabase reports it right now — not as the (possibly hour-old) access token describes it.</summary>
public sealed record SupabaseUser(
    SupabaseUserStatus Status,
    Guid Id = default,
    string? Email = null,
    bool EmailConfirmed = false,
    bool Banned = false,
    bool IsAnonymous = false)
{
    public static readonly SupabaseUser Revoked = new(SupabaseUserStatus.Revoked);
    public static readonly SupabaseUser Unavailable = new(SupabaseUserStatus.Unavailable);
}

/// <summary>Asks Supabase Auth who a (signature-checked) access token belongs to.</summary>
public interface ISupabaseUserDirectory
{
    Task<SupabaseUser> LookupAsync(string accessToken, CancellationToken ct);
}

/// <summary>
/// <c>GET {project}/auth/v1/user</c> with the user's own access token and the project's publishable (public) key.
/// Supabase answers from its database, so this sees a deleted or banned account, a revoked session and the current
/// e-mail confirmation state — none of which a still-unexpired JWT can show. Answers are cached per token for
/// <c>Auth:Supabase:UserCheckSeconds</c> (default 60, max 300): that is the longest a revoked account keeps access.
/// Failures (Unavailable) are never cached and never grant access.
/// </summary>
public sealed class SupabaseUserDirectory(IHttpClientFactory http, IConfiguration config, TimeProvider clock,
    ILogger<SupabaseUserDirectory> logger) : ISupabaseUserDirectory
{
    public const string HttpClientName = "supabase-user";
    private const int MaxEntries = 5000;
    private readonly ConcurrentDictionary<string, Entry> _cache = new();
    private readonly TimeSpan _ttl = TimeSpan.FromSeconds(Math.Clamp(config.GetValue("Auth:Supabase:UserCheckSeconds", 60), 0, 300));

    /// <summary>One answer per token; Lazy so that simultaneous requests (the dashboard loads several at once) share one call.</summary>
    private sealed record Entry(DateTimeOffset Expires, Lazy<Task<SupabaseUser>> Answer)
    {
        public bool UsableAt(DateTimeOffset now) => Expires > now && !(Answer.IsValueCreated && Answer.Value.IsCompletedSuccessfully
            && Answer.Value.Result.Status == SupabaseUserStatus.Unavailable);
    }

    public async Task<SupabaseUser> LookupAsync(string accessToken, CancellationToken ct)
    {
        if (_ttl <= TimeSpan.Zero) return await FetchAsync(accessToken).WaitAsync(ct);

        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(accessToken)));   // never keep raw tokens
        var now = clock.GetUtcNow();
        if (_cache.Count >= MaxEntries) Prune(now);
        Entry Fresh() => new(now + _ttl, new Lazy<Task<SupabaseUser>>(() => FetchAsync(accessToken)));
        var entry = _cache.AddOrUpdate(key, _ => Fresh(), (_, old) => old.UsableAt(now) ? old : Fresh());
        var answer = await entry.Answer.Value.WaitAsync(ct);
        if (answer.Status == SupabaseUserStatus.Unavailable) _cache.TryRemove(KeyValuePair.Create(key, entry));   // never cache outages
        return answer;
    }

    private void Prune(DateTimeOffset now)
    {
        foreach (var (k, v) in _cache)
            if (!v.UsableAt(now)) _cache.TryRemove(k, out _);
        if (_cache.Count >= MaxEntries) _cache.Clear();          // pathological burst: start over rather than grow
    }

    private async Task<SupabaseUser> FetchAsync(string accessToken)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, SupabaseAuth.Issuer(config) + "/user");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            req.Headers.Add("apikey", config["Auth:Supabase:PublishableKey"]);
            using var res = await http.CreateClient(HttpClientName).SendAsync(req);
            if (res.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
                return SupabaseUser.Revoked;
            if (!res.IsSuccessStatusCode)
            {
                logger.LogWarning("Supabase user check answered {Status}.", (int)res.StatusCode);
                return SupabaseUser.Unavailable;
            }
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStreamAsync());
            return Parse(doc.RootElement, clock.GetUtcNow());
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException)
        {
            logger.LogWarning(ex, "Supabase user check failed.");
            return SupabaseUser.Unavailable;
        }
    }

    /// <summary>Reads the fields that matter from Supabase's user object. Anything unexpected → Revoked (no access).</summary>
    public static SupabaseUser Parse(JsonElement u, DateTimeOffset now)
    {
        if (u.ValueKind != JsonValueKind.Object
            || !u.TryGetProperty("id", out var idEl) || !Guid.TryParse(idEl.GetString(), out var id))
            return SupabaseUser.Revoked;

        static string? Str(JsonElement o, string name) =>
            o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        var confirmedAt = Str(u, "email_confirmed_at");
        var bannedUntil = Str(u, "banned_until");
        var banned = bannedUntil is not null
            && (!DateTimeOffset.TryParse(bannedUntil, out var until) || until > now);          // unparseable → banned
        var anonymous = u.TryGetProperty("is_anonymous", out var an) && an.ValueKind == JsonValueKind.True;
        return new SupabaseUser(SupabaseUserStatus.Active, id, Str(u, "email"),
            EmailConfirmed: !string.IsNullOrEmpty(confirmedAt), Banned: banned, IsAnonymous: anonymous);
    }
}
