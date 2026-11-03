namespace NJTruckCharging.Domain.Entities;

/// <summary>
/// The team's site-SUITABILITY judgement for a candidate — not a sales/CRM pipeline stage. Independent of the
/// computed C3 score (never changes scoring or ranking).
/// </summary>
public enum ScreeningDecision
{
    NotReviewed = 0,
    Promising = 1,
    NeedsMoreResearch = 2,
    Unsuitable = 3
}

/// <summary>
/// Current screening decision for one candidate, with the evidence behind it. User-owned and soft-referenced by
/// the stable <see cref="CandidateSite.SiteId"/> (no FK) so it survives workbook re-imports. At most one per site;
/// every change is also appended to <see cref="SiteScreeningEntry"/> as an audit trail.
/// </summary>
public class SiteScreening
{
    public int Id { get; set; }
    public string SiteId { get; set; } = default!;
    public ScreeningDecision Decision { get; set; } = ScreeningDecision.NotReviewed;
    /// <summary>What was observed (satellite/Street View/records) that supports the decision.</summary>
    public string? Evidence { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

/// <summary>One historical screening assessment (append-only).</summary>
public class SiteScreeningEntry
{
    public int Id { get; set; }
    public string SiteId { get; set; } = default!;
    public ScreeningDecision Decision { get; set; }
    public string? Evidence { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
}
