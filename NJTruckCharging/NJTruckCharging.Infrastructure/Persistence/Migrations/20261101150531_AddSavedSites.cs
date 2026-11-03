using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NJTruckCharging.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSavedSites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SavedSites",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SiteId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    AssignedTo = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    TransferredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CrmItemId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavedSites", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SavedSites_SiteId",
                table: "SavedSites",
                column: "SiteId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SavedSites_Status",
                table: "SavedSites",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SavedSites");
        }
    }
}
