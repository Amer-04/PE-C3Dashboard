namespace NJTruckCharging.Domain.Entities;

/// <summary>One of the 10 largest FMCSA-registered carriers within 5 mi of a top-100 site ("Nearby fleets" sheet).</summary>
public class NearbyFleet
{
    public int Id { get; set; }
    public int CandidateSiteId { get; set; }
    public CandidateSite? CandidateSite { get; set; }

    public int Segment { get; set; }
    public int RankInSegment { get; set; }
    public string Carrier { get; set; } = default!;
    public string? Dba { get; set; }
    public int Trucks { get; set; }
    public int Tractors { get; set; }
    public int Drivers { get; set; }
    public string? CarrierAddress { get; set; }
    public double ApproxMiles { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? UsDot { get; set; }
    public string? Operation { get; set; }
    public string? Cargo { get; set; }
}
