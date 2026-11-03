using NJTruckCharging.Domain.Entities;

namespace NJTruckCharging.Domain.Scoring;

/// <summary>
/// The C3 site-scoring algorithm. Percentile components (fleets, truck traffic, warehouses) are ranked
/// within each NOFA segment because the NOFA scores applications against others in the same segment.
/// </summary>
public static class SiteScorer
{
    /// <summary>Scores every site (all segments) and assigns <see cref="CandidateSite.ComputedRank"/>.</summary>
    public static void ScoreAll(IEnumerable<CandidateSite> sites, ScoringRules? rules = null)
    {
        rules ??= ScoringRules.Default;
        foreach (var segment in sites.GroupBy(s => s.Segment))
            ScoreSegment(segment.ToList(), rules);
    }

    /// <summary>Scores the sites of one segment (percentiles are relative to this list).</summary>
    public static void ScoreSegment(IReadOnlyList<CandidateSite> sites, ScoringRules? rules = null)
    {
        rules ??= ScoringRules.Default;
        if (sites.Count == 0) return;

        var fleetPct = Percentiles(sites, s => s.TrucksBased5Mi);
        var trafficPct = Percentiles(sites, s => TruckTraffic(s));
        var warehousePct = Percentiles(sites, s => s.LargeWarehouses2Mi);

        for (var i = 0; i < sites.Count; i++)
        {
            var s = sites[i];
            var b = ScoreFixedComponents(s, rules);
            b.FleetsNearby = rules.FleetsMax * fleetPct[i];
            b.TruckTraffic = rules.TrafficMax * trafficPct[i];
            b.LargeWarehouses = rules.WarehousesMax * warehousePct[i];
            s.Breakdown = b;
            s.Score = Math.Round(b.Total, 1);
        }

        var rank = 1;
        // Same ordering as the workbook: rounded score, then closest to the exit.
        foreach (var s in sites.OrderByDescending(s => s.Score).ThenBy(s => s.MilesToExit))
            s.ComputedRank = rank++;
    }

    /// <summary>Everything except the three within-segment percentile components.</summary>
    public static ScoreBreakdown ScoreFixedComponents(CandidateSite s, ScoringRules? rules = null)
    {
        rules ??= ScoringRules.Default;
        return new ScoreBreakdown
        {
            ExitDistance = ExitDistancePoints(s.MilesToExit, rules),
            OpenLand = OpenLandPoints(s.OpenAcresEst, rules),
            PropertyClass = PropertyClassPoints(s.PropClass, rules),
            TruckingOnSite = s.FreightUse ? rules.FreightOnSite : s.IndustrialUse ? rules.OtherIndustrialOnSite : 0,
            TruckStop = s.TruckStops2Mi > 0 ? rules.TruckStopPoints : 0,
            Port = s.MilesToPort <= 5 ? rules.PortWithin5Mi : s.MilesToPort <= 10 ? rules.PortWithin10Mi : 0,
            NoTruckCharger = s.MilesToTruckDcfc > rules.NoTruckChargerMiles ? rules.NoTruckChargerFar : rules.NoTruckChargerNear,
            NoFastCharger = s.MilesToAnyDcfc > rules.NoFastChargerMiles ? rules.NoFastCharger : 0,
            LotShape = LotShapePoints(s.Compactness, s.ShortSideFt, rules),
            FarFromTruckRoad = s.MilesToTruckRoad > rules.FarFromTruckRoadMiles ? rules.FarFromTruckRoadPenalty : 0,
            Highlands = s.InHighlandsPreservation ? rules.HighlandsPreservationPenalty : 0,
            BusyMultiTenant = s.BusyMultiTenant ? rules.BusyMultiTenantPenalty : 0,
            Flood = s.InFloodZone || (!s.FloodChecked && rules.PenalizeUncheckedFlood) ? rules.FloodPenalty : 0
        };
    }

    public static int TruckTraffic(CandidateSite s) => Math.Max(s.TruckAadtAdjacent, s.TruckAadtAtExit);

    public static double ExitDistancePoints(double miles, ScoringRules rules)
    {
        if (miles <= rules.ExitFullMiles) return rules.ExitMax;
        var span = rules.ExitZeroMiles - rules.ExitFullMiles;
        return Math.Clamp(rules.ExitMax * (rules.ExitZeroMiles - miles) / span, 0, rules.ExitMax);
    }

    public static double OpenLandPoints(double openAcres, ScoringRules rules) => openAcres switch
    {
        >= 3 => rules.OpenLand3Ac,
        >= 1.5 => rules.OpenLand1_5Ac,
        >= 1 => rules.OpenLand1Ac,
        >= 0.6 => rules.OpenLand0_6Ac,
        _ => rules.OpenLandTiny
    };

    /// <summary>Best class among a merged site's lots: Industrial (4B) &gt; Vacant (1) &gt; Commercial (4A).</summary>
    public static double PropertyClassPoints(string? propClass, ScoringRules rules)
    {
        var parts = (propClass ?? "").Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Contains("4B", StringComparer.OrdinalIgnoreCase)) return rules.ClassIndustrial;
        if (parts.Contains("1")) return rules.ClassVacant;
        return rules.ClassCommercial;
    }

    public static double LotShapePoints(double compactness, double shortSideFt, ScoringRules rules)
    {
        if (shortSideFt < rules.ShapeNarrowShortSideFt) return rules.ShapeNarrow;
        if (compactness >= rules.ShapeMinCompactness && shortSideFt >= rules.ShapeGoodShortSideFt) return rules.ShapeGood;
        return 0;
    }

    /// <summary>
    /// Percentile rank in (0, 1] using the average rank of ties (same as pandas <c>rank(pct=True)</c>).
    /// </summary>
    public static double[] Percentiles(IReadOnlyList<CandidateSite> sites, Func<CandidateSite, double> selector)
    {
        var n = sites.Count;
        var values = new double[n];
        for (var i = 0; i < n; i++) values[i] = selector(sites[i]);

        var order = Enumerable.Range(0, n).OrderBy(i => values[i]).ToArray();
        var result = new double[n];
        var start = 0;
        while (start < n)
        {
            var end = start;
            while (end + 1 < n && values[order[end + 1]] == values[order[start]]) end++;
            var avgRank = (start + 1 + end + 1) / 2.0;   // 1-based average rank of the tie group
            for (var k = start; k <= end; k++) result[order[k]] = avgRank / n;
            start = end + 1;
        }
        return result;
    }

    /// <summary>
    /// Recovers the "six or more businesses on the parcel" flag, which the workbook does not store, by comparing
    /// the delivered score with the score computed without that penalty.
    /// </summary>
    public static bool InferBusyMultiTenant(CandidateSite scoredWithoutBusyFlag, double deliveredScore, ScoringRules? rules = null)
    {
        rules ??= ScoringRules.Default;
        var residual = deliveredScore - scoredWithoutBusyFlag.Breakdown.Total;
        return Math.Abs(residual - rules.BusyMultiTenantPenalty) < 0.5;
    }
}
