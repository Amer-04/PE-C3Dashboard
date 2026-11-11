using System.Collections.Concurrent;
using Microsoft.IdentityModel.JsonWebTokens;
using NJTruckCharging.Security;

namespace NJTruckCharging.Tests.Integration;

/// <summary>
/// Stands in for Supabase's user database: what <c>GET /auth/v1/user</c> would answer for a token. Tests change an
/// account (unconfirm, ban, delete, change e-mail, revoke one session, take Supabase down) and the next request sees it.
/// </summary>
public sealed class FakeSupabaseUsers : ISupabaseUserDirectory
{
    public sealed record Account(string Email, bool Confirmed = true, bool Banned = false, bool Deleted = false);

    private readonly ConcurrentDictionary<Guid, Account> _accounts = new();
    private readonly ConcurrentDictionary<string, bool> _revokedTokens = new();
    private int _lookups;

    /// <summary>Supabase unreachable: every lookup answers Unavailable.</summary>
    public bool Down { get; set; }

    /// <summary>How many times the API asked about a token (proves forged tokens never reach Supabase).</summary>
    public int Lookups => Volatile.Read(ref _lookups);

    public Guid Add(Guid id, string email, bool confirmed = true)
    {
        _accounts[id] = new Account(email, confirmed);
        return id;
    }

    public Guid Add(string email, bool confirmed = true) => Add(Guid.NewGuid(), email, confirmed);

    public string? EmailOf(Guid id) => _accounts.TryGetValue(id, out var a) ? a.Email : null;

    public void Update(Guid id, Func<Account, Account> change) => _accounts[id] = change(_accounts[id]);

    /// <summary>Signs out one session (e.g. "sign out everywhere" or a password change) without touching the account.</summary>
    public void RevokeSession(string token) => _revokedTokens[token] = true;

    public Task<SupabaseUser> LookupAsync(string accessToken, CancellationToken ct)
    {
        Interlocked.Increment(ref _lookups);
        if (Down) return Task.FromResult(SupabaseUser.Unavailable);
        if (_revokedTokens.ContainsKey(accessToken)) return Task.FromResult(SupabaseUser.Revoked);
        var id = Guid.Parse(new JsonWebToken(accessToken).Subject);
        if (!_accounts.TryGetValue(id, out var a) || a.Deleted) return Task.FromResult(SupabaseUser.Revoked);
        return Task.FromResult(new SupabaseUser(SupabaseUserStatus.Active, id, a.Email, a.Confirmed, a.Banned));
    }
}
