using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NJTruckCharging.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DropPipelineColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SavedSites_Status",
                table: "SavedSites");

            migrationBuilder.DropColumn(
                name: "AssignedTo",
                table: "SavedSites");

            migrationBuilder.DropColumn(
                name: "CrmItemId",
                table: "SavedSites");

            migrationBuilder.DropColumn(
                name: "NextAction",
                table: "SavedSites");

            migrationBuilder.DropColumn(
                name: "NextActionDueUtc",
                table: "SavedSites");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "SavedSites");

            migrationBuilder.DropColumn(
                name: "TransferredAtUtc",
                table: "SavedSites");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AssignedTo",
                table: "SavedSites",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CrmItemId",
                table: "SavedSites",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NextAction",
                table: "SavedSites",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NextActionDueUtc",
                table: "SavedSites",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "SavedSites",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "TransferredAtUtc",
                table: "SavedSites",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SavedSites_Status",
                table: "SavedSites",
                column: "Status");
        }
    }
}
