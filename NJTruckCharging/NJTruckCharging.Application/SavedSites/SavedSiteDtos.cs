namespace NJTruckCharging.Application.SavedSites;

/// <summary>Bookmark a candidate. Idempotent on <see cref="SiteId"/>.</summary>
public sealed record SaveSiteRequest(string SiteId, string? Notes);

/// <summary>Edit the note on a bookmark (PUT semantics).</summary>
public sealed record UpdateSavedSiteRequest(string? Notes);

/// <summary>
/// A bookmarked site plus the candidate display fields joined from <see cref="NJTruckCharging.Domain.Entities.CandidateSite"/>,
/// so the Saved view is useful without a second request. Candidate fields are null when the referenced
/// SiteId is no longer present (e.g. dropped by a re-import).
/// </summary>
public sealed record SavedSiteDto
{
    public required int Id { get; init; }
    public required string SiteId { get; init; }
    public string? Notes { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public required DateTimeOffset UpdatedAtUtc { get; init; }

    /// <summary>False when the referenced candidate is no longer in the dataset.</summary>
    public required bool CandidateFound { get; init; }

    // ---- Joined candidate fields (null when CandidateFound is false) ----
    public int? CandidateId { get; init; }
    public int? Segment { get; init; }
    public int? Rank { get; init; }
    public double? Score { get; init; }
    public string? Address { get; init; }
    public string? Municipality { get; init; }
    public string? County { get; init; }
    public string? PropClass { get; init; }
    public double? OpenAcres { get; init; }
    public double? MilesToExit { get; init; }
    public string? NearestHighway { get; init; }
    public string? OwnerName { get; init; }
    public string? OwnerMailAddress { get; init; }
    public string? OwnerMailCity { get; init; }
    public string? OwnerMailZip { get; init; }
    public string? BusinessPhone { get; init; }
    public double? Lat { get; init; }
    public double? Lng { get; init; }
}

/// <summary>Lightweight bookmark marker for the dashboard (SiteId → saved id).</summary>
public sealed record SavedSiteRef(int Id, string SiteId);
