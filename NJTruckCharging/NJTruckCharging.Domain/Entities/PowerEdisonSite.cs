namespace NJTruckCharging.Domain.Entities;

public enum PowerEdisonSiteStatus
{
    Operating = 0,
    LetterOfIntent = 1,
    Planned = 2
}

/// <summary>
/// Existing or pipeline EV Edison / Power Edison charging site. Candidates within 2 mi compete with it:
/// NJDEP may fund only one of two strong proposals in close proximity.
/// </summary>
public class PowerEdisonSite
{
    public int Id { get; set; }
    public string Name { get; set; } = default!;            // must match nearest_pe_site in the workbook
    public string ShortName { get; set; } = default!;
    public string? Address { get; set; }
    public string Municipality { get; set; } = default!;
    public int Segment { get; set; }
    public PowerEdisonSiteStatus Status { get; set; }
    public string? Notes { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
}
