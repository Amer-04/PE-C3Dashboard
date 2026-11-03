using NJTruckCharging.Domain.Entities;
using NJTruckCharging.Domain.Scoring;

namespace NJTruckCharging.Application.Sites;

/// <summary>Turns a <see cref="ScoreBreakdown"/> into labelled rows for the UI.</summary>
public static class ScoreLines
{
    public static IReadOnlyList<ScoreLineDto> For(CandidateSite s, int sitesInSegment)
    {
        var b = s.Breakdown;
        var traffic = SiteScorer.TruckTraffic(s);
        return
        [
            new("exit", "Distance to eligible exit", "Access", b.ExitDistance, 20, 0,
                $"{s.MilesToExit:0.00} mi to {s.NearestHighway} exit {s.NearestExit} (20 within 0.5 mi → 0 at 5 mi)"),
            new("openLand", "Open land", "Site", b.OpenLand, 20, -15,
                $"{s.OpenAcresEst:0.##} open ac of {s.Acres:0.##} ac (≥3 ac = 20)"),
            new("propertyClass", "Property class", "Site", b.PropertyClass, 15, 8,
                $"Class {s.PropClass} (Industrial 15 · Vacant 11 · Commercial 8)"),
            new("truckingOnSite", "Trucking on site", "Site", b.TruckingOnSite, 10, 0,
                s.FreightUse ? "Freight / logistics business on parcel" : s.IndustrialUse ? "Other industrial use on parcel" : "No trucking or industrial use listed"),
            new("fleets", "Fleets based nearby", "Truck demand", b.FleetsNearby, 12, 0,
                $"{s.TrucksBased5Mi:N0} trucks within 5 mi — {Pct(b.FleetsNearby / 12)} percentile of {sitesInSegment:N0} sites in segment"),
            new("traffic", "Truck traffic", "Truck demand", b.TruckTraffic, 10, 0,
                $"{traffic:N0} tractor-trailers/day — {Pct(b.TruckTraffic / 10)} percentile"),
            new("warehouses", "Large warehouses", "Truck demand", b.LargeWarehouses, 6, 0,
                $"{s.LargeWarehouses2Mi} buildings ≥200k sq ft within 2 mi — {Pct(b.LargeWarehouses / 6)} percentile"),
            new("truckStop", "Truck stop or parking", "Truck demand", b.TruckStop, 3, 0,
                $"{s.TruckStops2Mi} within 2 mi (nearest {s.MilesToTruckStop:0.0} mi)"),
            new("port", "Port", "Truck demand", b.Port, 4, 0,
                $"{s.MilesToPort:0.0} mi to {s.NearestPort}"),
            new("noTruckCharger", "No truck charger nearby", "Charger gap", b.NoTruckCharger, 10, 4,
                $"Nearest MHD charger {s.MilesToTruckDcfc:0.0} mi (>10 mi = 10)"),
            new("noFastCharger", "No fast charger nearby", "Charger gap", b.NoFastCharger, 5, 0,
                $"Nearest DCFC {s.MilesToAnyDcfc:0.0} mi — {s.NearestDcfc}"),
            new("lotShape", "Lot shape", "Site", b.LotShape, 5, -10,
                $"Short side {s.ShortSideFt:N0} ft, compactness {s.Compactness:0.00}"),
            new("truckRoad", "Far from truck road", "Penalty", b.FarFromTruckRoad, 0, -10,
                $"{s.MilesToTruckRoad:0.00} mi to a 500+ tractor-trailer/day road"),
            new("highlands", "Highlands Preservation", "Penalty", b.Highlands, 0, -15, s.Highlands ?? "Outside Highlands"),
            new("busy", "Busy multi-tenant", "Penalty", b.BusyMultiTenant, 0, -5,
                s.BusyMultiTenant ? "Six or more businesses on parcel" : "Fewer than six businesses"),
            new("flood", "FEMA 100-yr flood", "Penalty", b.Flood, 0, -10,
                !s.FloodChecked ? "Not checked (outside top 150) — penalised until verified"
                    : s.InFloodZone ? $"In flood zone {s.FloodZone100Yr}" : "Checked — not in flood zone")
        ];
    }

    /// <summary>0.92 → "92nd".</summary>
    private static string Pct(double fraction)
    {
        var n = (int)Math.Round(fraction * 100);
        var suffix = (n % 100) is 11 or 12 or 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
        return $"{n}{suffix}";
    }
}
