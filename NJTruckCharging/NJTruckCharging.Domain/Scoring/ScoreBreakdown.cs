namespace NJTruckCharging.Domain.Scoring;

/// <summary>Points earned by a site in every scoring category. Penalties are stored as negative numbers.</summary>
public class ScoreBreakdown
{
    // Positive points
    public double ExitDistance { get; set; }        // 0..20
    public double OpenLand { get; set; }            // -15..20
    public double PropertyClass { get; set; }       // 8..15
    public double TruckingOnSite { get; set; }      // 0..10
    public double FleetsNearby { get; set; }        // 0..12  (percentile in segment)
    public double TruckTraffic { get; set; }        // 0..10  (percentile in segment)
    public double LargeWarehouses { get; set; }     // 0..6   (percentile in segment)
    public double TruckStop { get; set; }           // 0..3
    public double Port { get; set; }                // 0..4
    public double NoTruckCharger { get; set; }      // 4..10
    public double NoFastCharger { get; set; }       // 0..5
    public double LotShape { get; set; }            // -10..5

    // Penalties
    public double FarFromTruckRoad { get; set; }    // 0 / -10
    public double Highlands { get; set; }           // 0 / -15
    public double BusyMultiTenant { get; set; }     // 0 / -5
    public double Flood { get; set; }               // 0 / -10

    public double TruckDemand => FleetsNearby + TruckTraffic + LargeWarehouses + TruckStop + Port;

    public double Total =>
        ExitDistance + OpenLand + PropertyClass + TruckingOnSite + TruckDemand +
        NoTruckCharger + NoFastCharger + LotShape +
        FarFromTruckRoad + Highlands + BusyMultiTenant + Flood;
}
