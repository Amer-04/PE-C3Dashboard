using NJTruckCharging.Application.Methodology;
using NJTruckCharging.Domain.Entities;
using NJTruckCharging.Domain.Methodology;
using NJTruckCharging.Domain.Scoring;

namespace NJTruckCharging.Application.Sites;

/// <summary>
/// Turns a site's points under the ACTIVE methodology into labelled rows for the UI. Labels, maxima and rule text
/// come from the active version's rules; points come from that version's stored per-site scores, so the breakdown
/// always adds up to the score shown in every other view.
/// </summary>
public static class ScoreLines
{
    public static IReadOnlyList<ScoreLineDto> For(CandidateSite s, int sitesInSegment, ActiveMethodology? active, SiteVersionScore? row,
        FieldCatalog? catalog = null)
    {
        if (active is null) return Legacy(s);
        var stored = row is not null && row.MethodologyVersionId == active.VersionId ? StoredPoints.Parse(row.PointsJson) : null;
        var lines = new List<ScoreLineDto>();
        for (var i = 0; i < active.Rules.Criteria.Count; i++)
        {
            var c = active.Rules.Criteria[i];
            if (!c.Enabled) continue;
            var view = MethodologyService.View(c, catalog);
            double points;
            var missing = false;
            if (stored is not null && i < stored.P.Length) { points = stored.P[i]; missing = stored.M.Contains(i); }
            else if (BaselineMethodology.BreakdownColumns.TryGetValue(c.Key, out var col))
                points = (double)typeof(ScoreBreakdown).GetProperty(col)!.GetValue(s.Breakdown)!;
            else points = 0;

            var basis = Context(c, s, sitesInSegment, points, catalog);
            if (missing) basis += $" — no data: missing-data policy applied ({CriterionText.Pts(c.Missing.Award)} pts)";
            lines.Add(new ScoreLineDto(c.Key, c.Name, CriterionText.CategoryLabel(c.Category), points, view.Max, view.Min,
                basis + " · Rule: " + CriterionText.Rule(c, catalog)));
        }
        return lines;
    }

    /// <summary>Site-specific context: the original descriptive text for built-in criteria, the raw inputs otherwise.</summary>
    private static string Context(Criterion c, CandidateSite s, int inSegment, double points, FieldCatalog? catalog)
    {
        var max = c.MaxPoints ?? 0;
        string Pct() => max == 0 ? "" : $" — {Ordinal(points / max)} percentile of {inSegment:N0} sites in segment";
        if (c.Kind == CriterionKind.Percentile) return CriterionText.SiteInputs(c, s, catalog) + Pct();
        return c.Key switch
        {
            "exit" when c.Field == "milesToExit" => $"{s.MilesToExit:0.00} mi to {s.NearestHighway} exit {s.NearestExit}",
            "openLand" => $"{s.OpenAcresEst:0.##} open ac of {s.Acres:0.##} ac",
            "propertyClass" => $"Class {s.PropClass}",
            "truckingOnSite" => s.FreightUse ? "Freight / logistics business on parcel" : s.IndustrialUse ? "Other industrial use on parcel" : "No trucking or industrial use listed",
            "truckStop" => $"{s.TruckStops2Mi} within 2 mi (nearest {s.MilesToTruckStop:0.0} mi)",
            "port" => $"{s.MilesToPort:0.0} mi to {s.NearestPort}",
            "noTruckCharger" => $"Nearest MHD charger {s.MilesToTruckDcfc:0.0} mi",
            "noFastCharger" => $"Nearest DCFC {s.MilesToAnyDcfc:0.0} mi — {s.NearestDcfc}",
            "lotShape" => $"Short side {s.ShortSideFt:N0} ft, compactness {s.Compactness:0.00}",
            "truckRoad" => $"{s.MilesToTruckRoad:0.00} mi to a 500+ tractor-trailer/day road",
            "highlands" => s.Highlands ?? "Outside Highlands",
            "busy" => s.BusyMultiTenant ? "Six or more businesses on parcel (inferred)" : "Fewer than six businesses",
            "flood" => !s.FloodChecked ? "Not checked (outside top 150)" : s.InFloodZone ? $"In flood zone {s.FloodZone100Yr}" : "Checked — not in flood zone",
            _ => CriterionText.SiteInputs(c, s, catalog)
        };
    }

    /// <summary>Fallback before the methodology is initialised: the stored built-in breakdown.</summary>
    private static IReadOnlyList<ScoreLineDto> Legacy(CandidateSite s)
    {
        var rules = BaselineMethodology.Create();
        return rules.Criteria.Select(c =>
        {
            var v = MethodologyService.View(c);
            var pts = (double)typeof(ScoreBreakdown).GetProperty(BaselineMethodology.BreakdownColumns[c.Key])!.GetValue(s.Breakdown)!;
            return new ScoreLineDto(c.Key, c.Name, CriterionText.CategoryLabel(c.Category), pts, v.Max, v.Min, CriterionText.Rule(c));
        }).ToList();
    }

    /// <summary>0.92 → "92nd".</summary>
    private static string Ordinal(double fraction)
    {
        var n = (int)Math.Round(fraction * 100);
        var suffix = (n % 100) is 11 or 12 or 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
        return $"{n}{suffix}";
    }
}
