namespace NJTruckCharging.Application.Methodology;

/// <summary>
/// Who is changing the methodology: the Supabase user id and the e-mail address Supabase verified for that account
/// (never the e-mail claimed in the token). Both are null for local trusted-network edits and system re-applications.
/// </summary>
public sealed record MethodologyActor(string? UserId, string? Email)
{
    public static readonly MethodologyActor System = new(null, null);
}

/// <summary>
/// One recalculation (preview, publish/restore, rescore) at a time in this API process. Recalculating scores all
/// ~30,000 sites in memory; overlapping jobs would only multiply memory and CPU use and then collide on the database
/// scoring lock anyway, so a second request is refused at once instead of queueing.
/// (The database advisory lock still serialises jobs across processes.)
/// </summary>
public sealed class ScoringJobGate
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Returns a lease to dispose when the job ends, or null if another job is running.</summary>
    public IDisposable? TryEnter() => _gate.Wait(0) ? new Lease(_gate) : null;

    private sealed class Lease(SemaphoreSlim gate) : IDisposable
    {
        private int _released;
        public void Dispose() { if (Interlocked.Exchange(ref _released, 1) == 0) gate.Release(); }
    }
}
