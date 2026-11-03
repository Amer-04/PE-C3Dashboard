namespace NJTruckCharging.Domain.Entities;

/// <summary>
/// A bookmarked candidate site — a user has flagged it as worth a closer look during C3 site discovery
/// and evaluation. This is a simple bookmark (optionally with a note), NOT a sales/CRM record. It is
/// user-owned and references the candidate by the stable business key <see cref="CandidateSite.SiteId"/>
/// (a soft reference, so it survives workbook re-imports). Display fields are joined from
/// <see cref="CandidateSite"/> at query time.
/// </summary>
public class SavedSite
{
    public int Id { get; set; }

    /// <summary>Stable candidate key (<see cref="CandidateSite.SiteId"/>). Unique: a site is bookmarked at most once.</summary>
    public string SiteId { get; set; } = default!;

    /// <summary>Optional free-text note on why the site is worth evaluating.</summary>
    public string? Notes { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
