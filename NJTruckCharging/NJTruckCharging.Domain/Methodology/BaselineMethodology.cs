using NJTruckCharging.Domain.Scoring;

namespace NJTruckCharging.Domain.Methodology;

/// <summary>
/// The original C3 workbook methodology expressed as rules — immutable baseline Version 1. Built from
/// <see cref="ScoringRules.Default"/> so the numbers have a single source; reproduces <see cref="SiteScorer"/>
/// (and therefore the workbook) exactly for all candidates.
/// </summary>
public static class BaselineMethodology
{
    public const string Name = "Original C3 workbook methodology";

    /// <summary>Built-in criterion key → the persisted <see cref="ScoreBreakdown"/> column it materialises into.</summary>
    public static readonly IReadOnlyDictionary<string, string> BreakdownColumns = new Dictionary<string, string>
    {
        ["exit"] = nameof(ScoreBreakdown.ExitDistance),
        ["openLand"] = nameof(ScoreBreakdown.OpenLand),
        ["propertyClass"] = nameof(ScoreBreakdown.PropertyClass),
        ["truckingOnSite"] = nameof(ScoreBreakdown.TruckingOnSite),
        ["fleets"] = nameof(ScoreBreakdown.FleetsNearby),
        ["traffic"] = nameof(ScoreBreakdown.TruckTraffic),
        ["warehouses"] = nameof(ScoreBreakdown.LargeWarehouses),
        ["truckStop"] = nameof(ScoreBreakdown.TruckStop),
        ["port"] = nameof(ScoreBreakdown.Port),
        ["noTruckCharger"] = nameof(ScoreBreakdown.NoTruckCharger),
        ["noFastCharger"] = nameof(ScoreBreakdown.NoFastCharger),
        ["lotShape"] = nameof(ScoreBreakdown.LotShape),
        ["truckRoad"] = nameof(ScoreBreakdown.FarFromTruckRoad),
        ["highlands"] = nameof(ScoreBreakdown.Highlands),
        ["busy"] = nameof(ScoreBreakdown.BusyMultiTenant),
        ["flood"] = nameof(ScoreBreakdown.Flood),
    };

    public static MethodologyRules Create() => Create(ScoringRules.Default);

    public static MethodologyRules Create(ScoringRules r) => new()
    {
        Criteria =
        [
            new()
            {
                Key = "exit", Name = "Distance to eligible exit", Category = CriterionCategory.Access, BuiltIn = true,
                Kind = CriterionKind.Linear, Field = "milesToExit", FullAt = r.ExitFullMiles, ZeroAt = r.ExitZeroMiles, MaxPoints = r.ExitMax,
                Explanation = "Full points within the full-points distance of an eligible highway exit, falling linearly to 0 at the zero-points distance."
            },
            new()
            {
                Key = "openLand", Name = "Open land", Category = CriterionCategory.Site, BuiltIn = true, Kind = CriterionKind.Cases,
                Explanation = "Lot area minus building footprints (estimated). Very small open areas are penalised.",
                Cases =
                [
                    Band("≥ 3 ac", "openAcresEst", ConditionOp.Gte, 3, r.OpenLand3Ac),
                    Band("1.5–3 ac", "openAcresEst", ConditionOp.Gte, 1.5, r.OpenLand1_5Ac),
                    Band("1–1.5 ac", "openAcresEst", ConditionOp.Gte, 1, r.OpenLand1Ac),
                    Band("0.6–1 ac", "openAcresEst", ConditionOp.Gte, 0.6, r.OpenLand0_6Ac),
                ],
                DefaultPoints = r.OpenLandTiny
            },
            new()
            {
                Key = "propertyClass", Name = "Property class", Category = CriterionCategory.Site, BuiltIn = true, Kind = CriterionKind.Cases,
                Explanation = "Best class among a merged site's lots: Industrial (4B), then Vacant (1), otherwise Commercial (4A).",
                Cases =
                [
                    new() { Label = "Industrial (4B)", Points = r.ClassIndustrial, Conditions = [new() { Field = "propClass", Op = ConditionOp.AnyLotIn, Values = ["4B"] }] },
                    new() { Label = "Vacant (1)", Points = r.ClassVacant, Conditions = [new() { Field = "propClass", Op = ConditionOp.AnyLotIn, Values = ["1"] }] },
                ],
                DefaultPoints = r.ClassCommercial
            },
            new()
            {
                Key = "truckingOnSite", Name = "Trucking on site", Category = CriterionCategory.Site, BuiltIn = true, Kind = CriterionKind.Cases,
                Explanation = "A freight/logistics business on the parcel scores highest; another industrial use scores less.",
                Cases =
                [
                    Flag("Freight / logistics business", "freightUse", r.FreightOnSite),
                    Flag("Other industrial use", "industrialUse", r.OtherIndustrialOnSite),
                ]
            },
            new()
            {
                Key = "fleets", Name = "Fleets based nearby", Category = CriterionCategory.TruckDemand, BuiltIn = true,
                Kind = CriterionKind.Percentile, Field = "trucksBased5Mi", MaxPoints = r.FleetsMax,
                Explanation = "Percentile (within the NOFA segment) of trucks based within 5 mi."
            },
            new()
            {
                Key = "traffic", Name = "Truck traffic", Category = CriterionCategory.TruckDemand, BuiltIn = true,
                Kind = CriterionKind.Percentile, Field = "truckTraffic", MaxPoints = r.TrafficMax,
                Explanation = "Percentile (within the segment) of the larger of adjacent-road and at-exit tractor-trailer AADT."
            },
            new()
            {
                Key = "warehouses", Name = "Large warehouses", Category = CriterionCategory.TruckDemand, BuiltIn = true,
                Kind = CriterionKind.Percentile, Field = "largeWarehouses2Mi", MaxPoints = r.WarehousesMax,
                Explanation = "Percentile (within the segment) of buildings ≥200k sq ft within 2 mi."
            },
            new()
            {
                Key = "truckStop", Name = "Truck stop or parking", Category = CriterionCategory.TruckDemand, BuiltIn = true, Kind = CriterionKind.Cases,
                Explanation = "Any truck stop or truck parking within 2 mi.",
                Cases = [Band("At least one within 2 mi", "truckStops2Mi", ConditionOp.Gt, 0, r.TruckStopPoints)]
            },
            new()
            {
                Key = "port", Name = "Port", Category = CriterionCategory.TruckDemand, BuiltIn = true, Kind = CriterionKind.Cases,
                Explanation = "Proximity to a port.",
                Cases =
                [
                    Band("≤ 5 mi", "milesToPort", ConditionOp.Lte, 5, r.PortWithin5Mi),
                    Band("≤ 10 mi", "milesToPort", ConditionOp.Lte, 10, r.PortWithin10Mi),
                ]
            },
            new()
            {
                Key = "noTruckCharger", Name = "No truck charger nearby", Category = CriterionCategory.ChargerGap, BuiltIn = true, Kind = CriterionKind.Cases,
                Explanation = "Rewards a gap in medium/heavy-duty charging.",
                Cases = [Band($"Nearest MHD charger > {r.NoTruckChargerMiles:0.##} mi", "milesToTruckDcfc", ConditionOp.Gt, r.NoTruckChargerMiles, r.NoTruckChargerFar)],
                DefaultPoints = r.NoTruckChargerNear
            },
            new()
            {
                Key = "noFastCharger", Name = "No fast charger nearby", Category = CriterionCategory.ChargerGap, BuiltIn = true, Kind = CriterionKind.Cases,
                Explanation = "Rewards a gap in DC fast charging.",
                Cases = [Band($"Nearest DCFC > {r.NoFastChargerMiles:0.##} mi", "milesToAnyDcfc", ConditionOp.Gt, r.NoFastChargerMiles, r.NoFastCharger)]
            },
            new()
            {
                Key = "lotShape", Name = "Lot shape", Category = CriterionCategory.Site, BuiltIn = true, Kind = CriterionKind.Cases,
                Explanation = "Narrow lots are penalised; compact lots with a wide short side earn a bonus.",
                Cases =
                [
                    Band($"Short side < {r.ShapeNarrowShortSideFt:0} ft", "shortSideFt", ConditionOp.Lt, r.ShapeNarrowShortSideFt, r.ShapeNarrow),
                    new()
                    {
                        Label = $"Compactness ≥ {r.ShapeMinCompactness:0.##} and short side ≥ {r.ShapeGoodShortSideFt:0} ft", Points = r.ShapeGood,
                        Conditions =
                        [
                            new() { Field = "compactness", Op = ConditionOp.Gte, Value = r.ShapeMinCompactness },
                            new() { Field = "shortSideFt", Op = ConditionOp.Gte, Value = r.ShapeGoodShortSideFt },
                        ]
                    },
                ]
            },
            new()
            {
                Key = "truckRoad", Name = "Far from truck road", Category = CriterionCategory.Penalty, BuiltIn = true, Kind = CriterionKind.Cases,
                Explanation = "Penalty when the site is far from a road carrying 500+ tractor-trailers/day.",
                Cases = [Band($"> {r.FarFromTruckRoadMiles:0.##} mi from a truck road", "milesToTruckRoad", ConditionOp.Gt, r.FarFromTruckRoadMiles, r.FarFromTruckRoadPenalty)]
            },
            new()
            {
                Key = "highlands", Name = "Highlands Preservation", Category = CriterionCategory.Penalty, BuiltIn = true, Kind = CriterionKind.Cases,
                Explanation = "New development is restricted in the Highlands Preservation Area.",
                Cases = [Flag("In Preservation Area", "inHighlandsPreservation", r.HighlandsPreservationPenalty)]
            },
            new()
            {
                Key = "busy", Name = "Busy multi-tenant", Category = CriterionCategory.Penalty, BuiltIn = true, Kind = CriterionKind.Cases,
                Explanation = "Six or more businesses on the parcel. The flag is inferred at import from the workbook's delivered score.",
                Cases = [Flag("Six or more businesses", "busyMultiTenant", r.BusyMultiTenantPenalty)]
            },
            new()
            {
                Key = "flood", Name = "FEMA 100-yr flood", Category = CriterionCategory.Penalty, BuiltIn = true, Kind = CriterionKind.Cases,
                Explanation = "Penalty inside a FEMA 100-year flood zone. Only the top ~150 per segment were checked; unchecked sites carry the penalty so they stay below the verified shortlist.",
                Cases = [new() { Label = "In flood zone", Points = r.FloodPenalty, Conditions = [new() { Field = "floodStatus", Op = ConditionOp.In, Values = ["InZone"] }] }],
                Missing = r.PenalizeUncheckedFlood
                    ? new MissingDataPolicy { Mode = MissingDataMode.FixedPoints, Points = r.FloodPenalty }
                    : new MissingDataPolicy()
            },
        ]
    };

    private static ScoreCase Band(string label, string field, ConditionOp op, double value, double points) =>
        new() { Label = label, Points = points, Conditions = [new() { Field = field, Op = op, Value = value }] };

    private static ScoreCase Flag(string label, string field, double points) =>
        new() { Label = label, Points = points, Conditions = [new() { Field = field, Op = ConditionOp.IsTrue }] };
}
