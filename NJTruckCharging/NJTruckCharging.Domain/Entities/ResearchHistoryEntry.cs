namespace NJTruckCharging.Domain.Entities;

/// <summary>What a research history entry is about.</summary>
public enum ResearchSubject
{
    /// <summary>The site itself (e.g. research started).</summary>
    Site = 0,
    /// <summary>One feasibility / verification check (<see cref="ResearchHistoryEntry.ItemKey"/>).</summary>
    Feasibility = 1,
    /// <summary>One researched contact (<see cref="ResearchHistoryEntry.ContactId"/>).</summary>
    Contact = 2
}

public enum ResearchAction
{
    /// <summary>The site was added to the research workflow ("Start research").</summary>
    Started = 0,
    /// <summary>First verdict recorded for a check, or the same verdict re-recorded with new evidence.</summary>
    Recorded = 1,
    /// <summary>An earlier non-Unknown verdict was replaced by a different one.</summary>
    Corrected = 2,
    ContactAdded = 3,
    ContactUpdated = 4,
    ContactRemoved = 5
}

/// <summary>
/// Append-only audit of site research: every feasibility verdict and contact change, with the evidence that was
/// recorded at that moment. Rows are never updated or deleted, so an overwritten verdict stays visible here.
/// Soft-referenced by <see cref="CandidateSite.SiteId"/> (no FK) like the other research tables, and kept
/// entirely separate from the C3 score.
/// </summary>
public class ResearchHistoryEntry
{
    public long Id { get; set; }
    public string SiteId { get; set; } = default!;
    public ResearchSubject Subject { get; set; }
    public ResearchAction Action { get; set; }

    public FeasibilityItemKey? ItemKey { get; set; }
    public int? ContactId { get; set; }

    /// <summary>Status before the change (feasibility or contact verification status, as text).</summary>
    public string? PreviousStatus { get; set; }
    /// <summary>Status after the change.</summary>
    public string? Status { get; set; }

    public string? Evidence { get; set; }
    public string? SourceUrl { get; set; }
    public DateOnly? EvidenceDate { get; set; }

    /// <summary>Reason for a correction, or a note on a conflict / research start.</summary>
    public string? Note { get; set; }

    /// <summary>Self-reported name (free text, not authenticated).</summary>
    public string? RecordedBy { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
}
