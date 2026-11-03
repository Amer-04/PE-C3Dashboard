using NJTruckCharging.Application.Abstractions;
using NJTruckCharging.Application.Import;
using NJTruckCharging.Domain.Entities;

namespace NJTruckCharging.Infrastructure.Excel;

/// <summary>Reads C3_candidate_sites_all_segments.xlsx (sheet and column names as produced by the site-screening model).</summary>
public sealed class CandidateWorkbookReader : IWorkbookReader
{
    public const string AllCandidatesSheet = "All candidates";
    public const string SummarySheet = "Summary";
    public const string FleetsSheet = "Nearby fleets (top 100s)";
    public const string InterchangesSheet = "Interchanges used";
    public const string MethodSheet = "Method and sources";

    public ParsedWorkbook Read(Stream xlsx)
    {
        var wb = new ParsedWorkbook();
        using var reader = new XlsxSheetReader(xlsx);

        foreach (var r in reader.ReadRows(AllCandidatesSheet))
            wb.Sites.Add(MapSite(r));

        if (reader.SheetNames.Contains(SummarySheet))
            foreach (var r in reader.ReadRows(SummarySheet))
                wb.Segments.Add(MapSegment(r));

        if (reader.SheetNames.Contains(FleetsSheet))
            foreach (var r in reader.ReadRows(FleetsSheet))
                wb.NearbyFleets.Add(MapFleet(r));

        if (reader.SheetNames.Contains(InterchangesSheet))
            foreach (var r in reader.ReadRows(InterchangesSheet))
                wb.Interchanges.Add(new Interchange
                {
                    Highway = r.Str("highway") ?? "",
                    Exit = r.Str("exit"),
                    Destination = r.Str("dest"),
                    Latitude = r.Dbl("lat"),
                    Longitude = r.Dbl("lon")
                });

        if (reader.SheetNames.Contains(MethodSheet))
        {
            var order = 0;
            foreach (var r in reader.ReadRows(MethodSheet))
                wb.MethodologyNotes.Add(new MethodologyNote
                {
                    SortOrder = ++order,
                    Topic = r.Str("item") ?? "",
                    Detail = r.Str("detail") ?? ""
                });
        }

        if (wb.Sites.Count == 0)
            throw new InvalidDataException($"No rows found in sheet '{AllCandidatesSheet}'.");
        return wb;
    }

    private static CandidateSite MapSite(XlsxRow r)
    {
        var yearBuilt = r.NInt("yr_built");
        return new CandidateSite
        {
            Segment = r.Int("segment"),
            RankInSegment = r.Int("rank_in_segment"),
            FileScore = r.Dbl("score"),
            SiteAddress = r.Str("site_address") ?? "(no address)",
            Municipality = r.Str("municipality") ?? "",
            County = r.Str("county") ?? "",
            Block = r.Str("block"),
            Lot = r.Str("lot"),
            Qual = r.Str("qual"),
            LotsInSite = r.Int("lots_in_site"),
            Acres = r.Dbl("acres"),
            RecordedAcres = r.Dbl("recorded_acres"),
            OpenAcresEst = r.Dbl("open_acres_est"),
            BuildingCoverage = r.Dbl("building_coverage"),
            PropClass = r.Str("prop_class") ?? "",
            Zoning = r.Str("zoning"),
            DemandSummary = r.Str("demand_summary"),
            TrucksBased5Mi = r.Int("trucks_based_5mi"),
            CarriersBased5Mi = r.Int("carriers_5mi"),
            TractorsBased5Mi = r.Int("tractors_based_5mi"),
            TrucksBased3Mi = r.Int("trucks_based_3mi"),
            TrucksBased10Mi = r.Int("trucks_based_10mi"),
            TruckAadtAdjacent = r.Int("truck_aadt_adjacent"),
            TruckAadtAtExit = r.Int("truck_aadt_at_exit"),
            MilesToTruckRoad = r.Dbl("miles_to_truck_road"),
            LargeWarehouses2Mi = r.Int("large_warehouses_2mi"),
            TruckStops2Mi = r.Int("truck_stops_2mi"),
            MilesToTruckStop = r.Dbl("miles_to_truck_stop"),
            MilesToPort = r.Dbl("miles_to_port"),
            NearestPort = r.Str("nearest_port"),
            BusinessesOnSite = r.Str("businesses_on_site"),
            BusinessCategories = r.Str("business_categories"),
            FreightUse = r.Bool("freight_use"),
            IndustrialUse = r.Bool("industrial_use"),
            BusinessPhone = r.Whole("business_phone"),
            BusinessWebsite = r.Str("business_website"),
            OwnerName = r.Str("owner_name"),
            OwnerMailAddress = r.Str("owner_mail_address"),
            OwnerMailCity = r.Str("owner_mail_city"),
            OwnerMailZip = r.Code("owner_mail_zip", 5),
            OwnerOccupiedHint = r.Bool("owner_occupied_hint"),
            NearestHighway = r.Str("nearest_highway"),
            NearestExit = r.Str("nearest_exit"),
            ExitName = r.Str("exit_name"),
            MilesToExit = r.Dbl("miles_to_exit"),
            MilesToTruckDcfc = r.Dbl("miles_to_truck_dcfc"),
            MilesToAnyDcfc = r.Dbl("miles_to_any_dcfc"),
            NearestDcfc = r.Str("nearest_dcfc"),
            NearestPowerEdisonSite = r.Str("nearest_pe_site"),
            MilesToPowerEdisonSite = r.Dbl("miles_to_pe_site"),
            Utility = r.Str("utility"),
            Highlands = r.Str("highlands"),
            FloodZone100Yr = r.Str("flood_zone_100yr"),
            KnownContaminatedSites = r.NInt("known_contaminated_site"),
            ShortSideFt = r.Dbl("short_side_ft"),
            Compactness = r.Dbl("compactness"),
            BuildingDescription = r.Str("bldg_desc"),
            LandDescription = r.Str("land_desc"),
            YearBuilt = yearBuilt is > 1700 and < 2100 ? yearBuilt : null,
            LandValue = r.Dbl("land_value"),
            ImprovementValue = r.Dbl("impr_value"),
            AnnualTax = r.Dbl("annual_tax"),
            LastSalePrice = r.Dbl("last_sale_price"),
            LastDeedDate = r.Code("last_deed_date", 6),
            MemberLots = r.Str("member_lots"),
            SiteId = r.Str("site_id") ?? r.Str("pams_pin") ?? Guid.NewGuid().ToString("N"),
            PamsPin = r.Str("pams_pin") ?? "",
            Latitude = r.Dbl("lat"),
            Longitude = r.Dbl("lon")
        };
    }

    private static SegmentInfo MapSegment(XlsxRow r) => new()
    {
        Id = r.Int("segment"),
        Counties = r.Str("counties") ?? "",
        Candidates = r.Int("candidates"),
        MergedSites = r.Int("merged_sites"),
        TruckingOnSite = r.Int("trucking_on_site"),
        Top100MedianMilesToExit = r.Dbl("top100_median_miles_to_exit"),
        Top100MedianOpenAcres = r.Dbl("top100_median_open_acres"),
        Top100TruckingOnSite = r.Int("top100_trucking_on_site"),
        Top100MedianTrucksBased5Mi = r.Dbl("top100_median_trucks_based_5mi"),
        Top100MedianTruckAadtExit = r.Dbl("top100_median_truck_aadt_exit"),
        Top100MergedSites = r.Int("top100_merged_sites"),
        Top100Flood = r.Int("top100_flood"),
        Top100Contaminated = r.Int("top100_contaminated"),
        Top100WithinTwoMilesOfPowerEdison = r.Int("top100_within_2mi_of_PE_site"),
        No1Site = r.Str("no1_site"),
        No1Municipality = r.Str("no1_municipality"),
        No1Businesses = r.Str("no1_businesses"),
        No1Highway = r.Str("no1_highway"),
        No1MilesToExit = r.NDbl("no1_miles_to_exit"),
        PowerEdisonSitesInSegment = r.Str("power_edison_sites_in_segment")
    };

    private static NearbyFleet MapFleet(XlsxRow r) => new()
    {
        Segment = r.Int("segment"),
        RankInSegment = r.Int("rank_in_segment"),
        Carrier = r.Str("carrier") ?? "",
        Dba = r.Str("dba"),
        Trucks = r.Int("trucks"),
        Tractors = r.Int("tractors"),
        Drivers = r.Int("drivers"),
        CarrierAddress = r.Str("carrier_address"),
        ApproxMiles = r.Dbl("approx_miles"),
        Phone = r.Whole("phone"),
        Email = r.Str("email"),
        UsDot = r.Whole("usdot"),
        Operation = r.Str("operation"),
        Cargo = r.Str("cargo")
    };
}
