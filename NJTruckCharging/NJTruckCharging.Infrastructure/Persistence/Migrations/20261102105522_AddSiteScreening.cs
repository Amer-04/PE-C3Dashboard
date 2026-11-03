using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NJTruckCharging.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSiteScreening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SiteScreeningEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SiteId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Decision = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    Evidence = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteScreeningEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SiteScreenings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SiteId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Decision = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    Evidence = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteScreenings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SiteScreeningEntries_SiteId_RecordedAtUtc",
                table: "SiteScreeningEntries",
                columns: new[] { "SiteId", "RecordedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SiteScreenings_Decision",
                table: "SiteScreenings",
                column: "Decision");

            migrationBuilder.CreateIndex(
                name: "IX_SiteScreenings_SiteId",
                table: "SiteScreenings",
                column: "SiteId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SiteScreeningEntries");

            migrationBuilder.DropTable(
                name: "SiteScreenings");
        }
    }
}
