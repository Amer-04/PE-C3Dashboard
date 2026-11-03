using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NJTruckCharging.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CandidateSites",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SiteId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PamsPin = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Segment = table.Column<int>(type: "int", nullable: false),
                    RankInSegment = table.Column<int>(type: "int", nullable: false),
                    FileScore = table.Column<double>(type: "float", nullable: false),
                    SiteAddress = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Municipality = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    County = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Block = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Lot = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Qual = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Latitude = table.Column<double>(type: "float", nullable: false),
                    Longitude = table.Column<double>(type: "float", nullable: false),
                    LotsInSite = table.Column<int>(type: "int", nullable: false),
                    MemberLots = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Acres = table.Column<double>(type: "float", nullable: false),
                    RecordedAcres = table.Column<double>(type: "float", nullable: false),
                    OpenAcresEst = table.Column<double>(type: "float", nullable: false),
                    BuildingCoverage = table.Column<double>(type: "float", nullable: false),
                    PropClass = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Zoning = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ShortSideFt = table.Column<double>(type: "float", nullable: false),
                    Compactness = table.Column<double>(type: "float", nullable: false),
                    BuildingDescription = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    LandDescription = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    YearBuilt = table.Column<int>(type: "int", nullable: true),
                    LandValue = table.Column<double>(type: "float", nullable: false),
                    ImprovementValue = table.Column<double>(type: "float", nullable: false),
                    AnnualTax = table.Column<double>(type: "float", nullable: false),
                    LastSalePrice = table.Column<double>(type: "float", nullable: false),
                    LastDeedDate = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    OwnerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    OwnerMailAddress = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    OwnerMailCity = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    OwnerMailZip = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    OwnerOccupiedHint = table.Column<bool>(type: "bit", nullable: false),
                    BusinessesOnSite = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    BusinessCategories = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    BusinessPhone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    BusinessWebsite = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FreightUse = table.Column<bool>(type: "bit", nullable: false),
                    IndustrialUse = table.Column<bool>(type: "bit", nullable: false),
                    DemandSummary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    TrucksBased3Mi = table.Column<int>(type: "int", nullable: false),
                    TrucksBased5Mi = table.Column<int>(type: "int", nullable: false),
                    TrucksBased10Mi = table.Column<int>(type: "int", nullable: false),
                    CarriersBased5Mi = table.Column<int>(type: "int", nullable: false),
                    TractorsBased5Mi = table.Column<int>(type: "int", nullable: false),
                    TruckAadtAdjacent = table.Column<int>(type: "int", nullable: false),
                    TruckAadtAtExit = table.Column<int>(type: "int", nullable: false),
                    MilesToTruckRoad = table.Column<double>(type: "float", nullable: false),
                    LargeWarehouses2Mi = table.Column<int>(type: "int", nullable: false),
                    TruckStops2Mi = table.Column<int>(type: "int", nullable: false),
                    MilesToTruckStop = table.Column<double>(type: "float", nullable: false),
                    MilesToPort = table.Column<double>(type: "float", nullable: false),
                    NearestPort = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    NearestHighway = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    NearestExit = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ExitName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    MilesToExit = table.Column<double>(type: "float", nullable: false),
                    MilesToTruckDcfc = table.Column<double>(type: "float", nullable: false),
                    MilesToAnyDcfc = table.Column<double>(type: "float", nullable: false),
                    NearestDcfc = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    NearestPowerEdisonSite = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    MilesToPowerEdisonSite = table.Column<double>(type: "float", nullable: false),
                    Utility = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Highlands = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    FloodZone100Yr = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    KnownContaminatedSites = table.Column<int>(type: "int", nullable: true),
                    BusyMultiTenant = table.Column<bool>(type: "bit", nullable: false),
                    FloodChecked = table.Column<bool>(type: "bit", nullable: false),
                    InFloodZone = table.Column<bool>(type: "bit", nullable: false),
                    InHighlandsPreservation = table.Column<bool>(type: "bit", nullable: false),
                    IsContaminated = table.Column<bool>(type: "bit", nullable: false),
                    NearPowerEdisonSite = table.Column<bool>(type: "bit", nullable: false),
                    Breakdown_ExitDistance = table.Column<double>(type: "float", nullable: false),
                    Breakdown_OpenLand = table.Column<double>(type: "float", nullable: false),
                    Breakdown_PropertyClass = table.Column<double>(type: "float", nullable: false),
                    Breakdown_TruckingOnSite = table.Column<double>(type: "float", nullable: false),
                    Breakdown_FleetsNearby = table.Column<double>(type: "float", nullable: false),
                    Breakdown_TruckTraffic = table.Column<double>(type: "float", nullable: false),
                    Breakdown_LargeWarehouses = table.Column<double>(type: "float", nullable: false),
                    Breakdown_TruckStop = table.Column<double>(type: "float", nullable: false),
                    Breakdown_Port = table.Column<double>(type: "float", nullable: false),
                    Breakdown_NoTruckCharger = table.Column<double>(type: "float", nullable: false),
                    Breakdown_NoFastCharger = table.Column<double>(type: "float", nullable: false),
                    Breakdown_LotShape = table.Column<double>(type: "float", nullable: false),
                    Breakdown_FarFromTruckRoad = table.Column<double>(type: "float", nullable: false),
                    Breakdown_Highlands = table.Column<double>(type: "float", nullable: false),
                    Breakdown_BusyMultiTenant = table.Column<double>(type: "float", nullable: false),
                    Breakdown_Flood = table.Column<double>(type: "float", nullable: false),
                    Score = table.Column<double>(type: "float", nullable: false),
                    ComputedRank = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CandidateSites", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Interchanges",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Highway = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Exit = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Destination = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Latitude = table.Column<double>(type: "float", nullable: false),
                    Longitude = table.Column<double>(type: "float", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Interchanges", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MethodologyNotes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Topic = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Detail = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MethodologyNotes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PowerEdisonSites",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ShortName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Address = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Municipality = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Segment = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Latitude = table.Column<double>(type: "float", nullable: false),
                    Longitude = table.Column<double>(type: "float", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PowerEdisonSites", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Segments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Counties = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Candidates = table.Column<int>(type: "int", nullable: false),
                    MergedSites = table.Column<int>(type: "int", nullable: false),
                    TruckingOnSite = table.Column<int>(type: "int", nullable: false),
                    Top100MedianMilesToExit = table.Column<double>(type: "float", nullable: false),
                    Top100MedianOpenAcres = table.Column<double>(type: "float", nullable: false),
                    Top100TruckingOnSite = table.Column<int>(type: "int", nullable: false),
                    Top100MedianTrucksBased5Mi = table.Column<double>(type: "float", nullable: false),
                    Top100MedianTruckAadtExit = table.Column<double>(type: "float", nullable: false),
                    Top100MergedSites = table.Column<int>(type: "int", nullable: false),
                    Top100Flood = table.Column<int>(type: "int", nullable: false),
                    Top100Contaminated = table.Column<int>(type: "int", nullable: false),
                    Top100WithinTwoMilesOfPowerEdison = table.Column<int>(type: "int", nullable: false),
                    No1Site = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    No1Municipality = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    No1Businesses = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    No1Highway = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    No1MilesToExit = table.Column<double>(type: "float", nullable: true),
                    PowerEdisonSitesInSegment = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Segments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NearbyFleets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CandidateSiteId = table.Column<int>(type: "int", nullable: false),
                    Segment = table.Column<int>(type: "int", nullable: false),
                    RankInSegment = table.Column<int>(type: "int", nullable: false),
                    Carrier = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Dba = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Trucks = table.Column<int>(type: "int", nullable: false),
                    Tractors = table.Column<int>(type: "int", nullable: false),
                    Drivers = table.Column<int>(type: "int", nullable: false),
                    CarrierAddress = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ApproxMiles = table.Column<double>(type: "float", nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    UsDot = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Operation = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Cargo = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NearbyFleets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NearbyFleets_CandidateSites_CandidateSiteId",
                        column: x => x.CandidateSiteId,
                        principalTable: "CandidateSites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CandidateSites_County",
                table: "CandidateSites",
                column: "County");

            migrationBuilder.CreateIndex(
                name: "IX_CandidateSites_Score",
                table: "CandidateSites",
                column: "Score");

            migrationBuilder.CreateIndex(
                name: "IX_CandidateSites_Segment_ComputedRank",
                table: "CandidateSites",
                columns: new[] { "Segment", "ComputedRank" });

            migrationBuilder.CreateIndex(
                name: "IX_CandidateSites_SiteId",
                table: "CandidateSites",
                column: "SiteId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NearbyFleets_CandidateSiteId",
                table: "NearbyFleets",
                column: "CandidateSiteId");

            migrationBuilder.CreateIndex(
                name: "IX_PowerEdisonSites_Name",
                table: "PowerEdisonSites",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Interchanges");

            migrationBuilder.DropTable(
                name: "MethodologyNotes");

            migrationBuilder.DropTable(
                name: "NearbyFleets");

            migrationBuilder.DropTable(
                name: "PowerEdisonSites");

            migrationBuilder.DropTable(
                name: "Segments");

            migrationBuilder.DropTable(
                name: "CandidateSites");
        }
    }
}
