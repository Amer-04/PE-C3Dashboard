namespace NJTruckCharging.Domain.Entities;

/// <summary>Eligible interstate interchange (OpenStreetMap motorway_junction) – "Interchanges used" sheet.</summary>
public class Interchange
{
    public int Id { get; set; }
    public string Highway { get; set; } = default!;
    public string? Exit { get; set; }
    public string? Destination { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
}
