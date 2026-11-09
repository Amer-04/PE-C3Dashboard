using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NJTruckCharging.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMethodologyVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ScoreVersionId",
                table: "CandidateSites",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "TruckDemandPoints",
                table: "CandidateSites",
                type: "float",
                nullable: false,
                defaultValue: 0.0);

            // Existing rows: truck-demand subtotal of the original methodology (values unchanged; Score/Rank untouched).
            migrationBuilder.Sql(
                "UPDATE CandidateSites SET TruckDemandPoints = Breakdown_FleetsNearby + Breakdown_TruckTraffic + Breakdown_LargeWarehouses + Breakdown_TruckStop + Breakdown_Port;");

            migrationBuilder.CreateTable(
                name: "MethodologyActivations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    VersionId = table.Column<int>(type: "int", nullable: false),
                    PreviousVersionId = table.Column<int>(type: "int", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SitesScored = table.Column<int>(type: "int", nullable: false),
                    DataVersion = table.Column<int>(type: "int", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MethodologyActivations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MethodologyVersions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IsBaseline = table.Column<bool>(type: "bit", nullable: false),
                    RulesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RulesHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ChangeSummary = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    BasedOnVersionId = table.Column<int>(type: "int", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PublishedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PreviewRulesHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ScoresDataVersion = table.Column<int>(type: "int", nullable: true),
                    PreviewedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PreviewSummaryJson = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MethodologyVersions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MethodologyState",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    ActiveVersionId = table.Column<int>(type: "int", nullable: true),
                    PreviousActiveVersionId = table.Column<int>(type: "int", nullable: true),
                    ActivatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DataVersion = table.Column<int>(type: "int", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MethodologyState", x => x.Id);
                    table.CheckConstraint("CK_MethodologyState_Singleton", "[Id] = 1");
                    table.ForeignKey(
                        name: "FK_MethodologyState_MethodologyVersions_ActiveVersionId",
                        column: x => x.ActiveVersionId,
                        principalTable: "MethodologyVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MethodologyState_MethodologyVersions_PreviousActiveVersionId",
                        column: x => x.PreviousActiveVersionId,
                        principalTable: "MethodologyVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SiteVersionScores",
                columns: table => new
                {
                    MethodologyVersionId = table.Column<int>(type: "int", nullable: false),
                    SiteId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Segment = table.Column<int>(type: "int", nullable: false),
                    Score = table.Column<double>(type: "float", nullable: false),
                    Rank = table.Column<int>(type: "int", nullable: false),
                    TruckDemandPoints = table.Column<double>(type: "float", nullable: false),
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
                    PointsJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteVersionScores", x => new { x.MethodologyVersionId, x.SiteId });
                    table.ForeignKey(
                        name: "FK_SiteVersionScores_MethodologyVersions_MethodologyVersionId",
                        column: x => x.MethodologyVersionId,
                        principalTable: "MethodologyVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MethodologyActivations_VersionId",
                table: "MethodologyActivations",
                column: "VersionId");

            migrationBuilder.CreateIndex(
                name: "IX_MethodologyState_ActiveVersionId",
                table: "MethodologyState",
                column: "ActiveVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_MethodologyState_PreviousActiveVersionId",
                table: "MethodologyState",
                column: "PreviousActiveVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_MethodologyVersions_IsBaseline",
                table: "MethodologyVersions",
                column: "IsBaseline",
                unique: true,
                filter: "[IsBaseline] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_SiteVersionScores_MethodologyVersionId_Segment_Rank",
                table: "SiteVersionScores",
                columns: new[] { "MethodologyVersionId", "Segment", "Rank" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MethodologyActivations");

            migrationBuilder.DropTable(
                name: "MethodologyState");

            migrationBuilder.DropTable(
                name: "SiteVersionScores");

            migrationBuilder.DropTable(
                name: "MethodologyVersions");

            migrationBuilder.DropColumn(
                name: "ScoreVersionId",
                table: "CandidateSites");

            migrationBuilder.DropColumn(
                name: "TruckDemandPoints",
                table: "CandidateSites");
        }
    }
}
