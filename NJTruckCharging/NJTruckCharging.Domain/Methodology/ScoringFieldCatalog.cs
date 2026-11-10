using NJTruckCharging.Domain.Entities;


namespace NJTruckCharging.Domain.Methodology;

public enum FieldType
{
    Number,
    Boolean,
    /// <summary>One category per site (In / NotIn).</summary>
    Category,
    /// <summary>A merged site lists one category per lot ("1/4B"); AnyLotIn matches if any lot is in the set.</summary>
    LotCategory
}

/// <summary>A site value as seen by the scoring engine. <see cref="IsMissing"/> = no source value (never imputed).</summary>
public readonly record struct FieldValue(double? Number, bool? Flag, string? Text, string[]? Lots)
{
    public bool IsMissing => Number is null && Flag is null && Text is null && Lots is null;

    public static FieldValue Num(double? v) => new(v, null, null, null);
    public static FieldValue Bool(bool v) => new(null, v, null, null);
    public static FieldValue Cat(string? v) => new(null, null, string.IsNullOrWhiteSpace(v) ? null : v.Trim(), null);
    public static FieldValue LotCats(string? v)
    {
        var parts = (v ?? "").Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return new(null, null, null, parts.Length == 0 ? null : parts);
    }
}

/// <summary>A source-data attribute that methodology criteria may score.</summary>
public sealed record ScoringField(
    string Key,
    string Label,
    FieldType Type,
    string? Unit,
    string Group,
    string Description,
    /// <summary>How missing values arise for this field (null = the workbook supplies it for every site).</summary>
    string? MissingMeaning,
    /// <summary>Allowed category values (Category / LotCategory).</summary>
    IReadOnlyList<string>? Categories,
    Func<CandidateSite, FieldValue> Read)
{
    public bool CanBeMissing => MissingMeaning is not null;
    /// <summary>User-defined custom field (values recorded per site or imported), not workbook source data.</summary>
    public bool IsCustom { get; init; }
    public CustomFieldSource? CustomSource { get; init; }
}

/// <summary>
/// The only attributes a methodology can use. Deliberately excludes contact/owner/business details, addresses,
/// coordinates, free-text zoning and all team research (feasibility, contacts, screening) — the C3 score is
/// computed from source data only.
/// <para>Missing data: the workbook reader turns blank numeric cells into 0, so only fields whose blanks are stored
/// as null (verified against the workbook: none of the numeric scoring columns has a blank) are offered as
/// nullable. A null is reported as missing and handled by the criterion's explicit missing-data policy.</para>
/// </summary>
public static class ScoringFieldCatalog
{
    public static readonly IReadOnlyList<string> PropertyClasses = ["1", "4A", "4B"];
    public static readonly IReadOnlyList<string> FloodStatuses = ["InZone", "Clear"];
    public static readonly IReadOnlyList<string> HighlandsAreas = ["Preservation", "Planning", "Outside"];

    public static readonly IReadOnlyList<ScoringField> All =
    [
        // ---- Access ----------------------------------------------------------------------------------------
        Num("milesToExit", "Distance to eligible exit", "mi", "Access", "Straight-line miles to the nearest eligible highway exit.", s => s.MilesToExit),
        Num("milesToTruckRoad", "Distance to truck road", "mi", "Access", "Miles to the nearest road carrying 500+ tractor-trailers/day.", s => s.MilesToTruckRoad),
        Num("milesToPort", "Distance to port", "mi", "Access", "Miles to the nearest port.", s => s.MilesToPort),
        Num("milesToPowerEdisonSite", "Distance to Power Edison site", "mi", "Access", "Miles to the nearest Power Edison / EV Edison site.", s => s.MilesToPowerEdisonSite),

        // ---- Site ------------------------------------------------------------------------------------------
        Num("openAcresEst", "Open land (estimated)", "ac", "Site", "Lot area minus building footprints (estimate).", s => s.OpenAcresEst),
        Num("acres", "Mapped parcel area", "ac", "Site", "Mapped area of the (merged) site.", s => s.Acres),
        Num("recordedAcres", "Recorded parcel area", "ac", "Site", "Area recorded in the tax record (0 = not recorded).", s => s.RecordedAcres),
        Num("buildingCoverage", "Building coverage", "fraction", "Site", "Share of the lot covered by buildings (0–1).", s => s.BuildingCoverage),
        Num("shortSideFt", "Lot short side", "ft", "Site", "Short side of the lot's minimum bounding rectangle.", s => s.ShortSideFt),
        Num("compactness", "Lot compactness", "0–1", "Site", "Shape compactness (1 = square/circle).", s => s.Compactness),
        Num("lotsInSite", "Lots in site", "lots", "Site", "Number of tax lots merged into this site.", s => s.LotsInSite),
        new("propClass", "Property class (per lot)", FieldType.LotCategory, null, "Site",
            "Tax class of each lot: 1 vacant, 4A commercial, 4B industrial. Merged sites list several (\"1/4B\").",
            null, PropertyClasses, s => FieldValue.LotCats(s.PropClass)),
        Flag("freightUse", "Freight / logistics business on parcel", "Site", "A freight or logistics business is listed on the parcel.", s => s.FreightUse),
        Flag("industrialUse", "Other industrial use on parcel", "Site", "Another industrial business is listed on the parcel.", s => s.IndustrialUse),
        Flag("ownerOccupiedHint", "Owner-occupied hint", "Site", "Owner mailing address matches the site (hint only).", s => s.OwnerOccupiedHint),
        Flag("busyMultiTenant", "Busy multi-tenant (inferred)", "Site",
            "Six or more businesses on the parcel. INFERRED at import from the workbook's delivered score (the workbook does not store the count).",
            s => s.BusyMultiTenant),
        new("yearBuilt", "Year built", FieldType.Number, "year", "Site", "Year the main building was built.",
            "Blank in the tax record (vacant land or not recorded).", null, s => FieldValue.Num(s.YearBuilt)),

        // ---- Truck demand ----------------------------------------------------------------------------------
        Num("trucksBased3Mi", "Trucks based within 3 mi", "trucks", "Truck demand", "FMCSA power units based within 3 miles.", s => s.TrucksBased3Mi),
        Num("trucksBased5Mi", "Trucks based within 5 mi", "trucks", "Truck demand", "FMCSA power units based within 5 miles.", s => s.TrucksBased5Mi),
        Num("trucksBased10Mi", "Trucks based within 10 mi", "trucks", "Truck demand", "FMCSA power units based within 10 miles.", s => s.TrucksBased10Mi),
        Num("carriersBased5Mi", "Carriers based within 5 mi", "carriers", "Truck demand", "Motor carriers based within 5 miles.", s => s.CarriersBased5Mi),
        Num("tractorsBased5Mi", "Tractors based within 5 mi", "tractors", "Truck demand", "Tractors based within 5 miles.", s => s.TractorsBased5Mi),
        Num("truckAadtAdjacent", "Truck traffic on adjacent road", "trucks/day", "Truck demand", "Tractor-trailer AADT on the adjacent road.", s => s.TruckAadtAdjacent),
        Num("truckAadtAtExit", "Truck traffic at exit", "trucks/day", "Truck demand", "Tractor-trailer AADT at the nearest exit.", s => s.TruckAadtAtExit),
        Num("truckTraffic", "Truck traffic (max of adjacent / exit)", "trucks/day", "Truck demand",
            "The larger of adjacent-road and at-exit tractor-trailer AADT (as the original methodology uses).",
            s => Math.Max(s.TruckAadtAdjacent, s.TruckAadtAtExit)),
        Num("largeWarehouses2Mi", "Large warehouses within 2 mi", "buildings", "Truck demand", "Buildings ≥200k sq ft within 2 miles.", s => s.LargeWarehouses2Mi),
        Num("truckStops2Mi", "Truck stops within 2 mi", "stops", "Truck demand", "Truck stops / truck parking within 2 miles.", s => s.TruckStops2Mi),
        Num("milesToTruckStop", "Distance to truck stop", "mi", "Truck demand", "Miles to the nearest truck stop / truck parking.", s => s.MilesToTruckStop),

        // ---- Charger gap -----------------------------------------------------------------------------------
        Num("milesToTruckDcfc", "Distance to truck-capable charger", "mi", "Charger gap", "Miles to the nearest MHD-capable charger.", s => s.MilesToTruckDcfc),
        Num("milesToAnyDcfc", "Distance to any DC fast charger", "mi", "Charger gap", "Miles to the nearest DC fast charger.", s => s.MilesToAnyDcfc),

        // ---- Constraints -----------------------------------------------------------------------------------
        new("floodStatus", "FEMA 100-yr flood status", FieldType.Category, null, "Constraints",
            "InZone = inside a FEMA 100-year flood zone; Clear = checked and outside.",
            "Only the top ~150 sites per segment were checked against FEMA maps; all others are 'not checked' (missing).",
            FloodStatuses, s => !s.FloodChecked ? FieldValue.Cat(null) : FieldValue.Cat(s.InFloodZone ? "InZone" : "Clear")),
        Flag("inHighlandsPreservation", "In Highlands Preservation Area", "Constraints", "Inside the NJ Highlands Preservation Area.", s => s.InHighlandsPreservation),
        new("highlandsArea", "Highlands area", FieldType.Category, null, "Constraints",
            "Preservation / Planning area of the NJ Highlands; the workbook leaves the cell blank for sites outside the region (reported as Outside).",
            null, HighlandsAreas,
            s => FieldValue.Cat(s.Highlands is null ? "Outside"
                : s.Highlands.Contains("Preservation", StringComparison.OrdinalIgnoreCase) ? "Preservation"
                : s.Highlands.Contains("Planning", StringComparison.OrdinalIgnoreCase) ? "Planning" : "Outside")),
        new("knownContaminatedSites", "Known contaminated sites on parcel", FieldType.Number, "sites", "Constraints",
            "NJDEP known contaminated sites on the parcel.",
            "Checked only for the top ~150 sites per segment; blank (missing) elsewhere.", null,
            s => FieldValue.Num(s.KnownContaminatedSites)),
        Flag("nearPowerEdisonSite", "Within 2 mi of a Power Edison site", "Constraints",
            "NJDEP may fund only one of two close proposals; the original methodology flags but does not score this.", s => s.NearPowerEdisonSite),

        // ---- Assessment (tax record) -----------------------------------------------------------------------
        Num("landValue", "Assessed land value", "$", "Assessment", "Assessed land value (0 may mean not recorded).", s => s.LandValue),
        Num("improvementValue", "Assessed improvement value", "$", "Assessment", "Assessed improvement value (0 may mean not recorded).", s => s.ImprovementValue),
        Num("annualTax", "Annual property tax", "$", "Assessment", "Annual tax (0 may mean not recorded).", s => s.AnnualTax),
        Num("lastSalePrice", "Last sale price", "$", "Assessment", "Last recorded sale price (0 may mean not recorded / nominal).", s => s.LastSalePrice),
    ];

    private static readonly Dictionary<string, ScoringField> ByKey = All.ToDictionary(f => f.Key, StringComparer.Ordinal);

    public static ScoringField? Find(string? key) => key is not null && ByKey.TryGetValue(key, out var f) ? f : null;

    public static IReadOnlyList<ConditionOp> OperatorsFor(FieldType t) => t switch
    {
        FieldType.Number => [ConditionOp.Lt, ConditionOp.Lte, ConditionOp.Gt, ConditionOp.Gte, ConditionOp.Eq, ConditionOp.Neq, ConditionOp.Between],
        FieldType.Boolean => [ConditionOp.IsTrue, ConditionOp.IsFalse],
        FieldType.Category => [ConditionOp.In, ConditionOp.NotIn],
        FieldType.LotCategory => [ConditionOp.AnyLotIn],
        _ => []
    };

    private static ScoringField Num(string key, string label, string unit, string group, string desc, Func<CandidateSite, double> read) =>
        new(key, label, FieldType.Number, unit, group, desc, null, null, s => FieldValue.Num(read(s)));

    private static ScoringField Flag(string key, string label, string group, string desc, Func<CandidateSite, bool> read) =>
        new(key, label, FieldType.Boolean, null, group, desc, null, null, s => FieldValue.Bool(read(s)));
}
