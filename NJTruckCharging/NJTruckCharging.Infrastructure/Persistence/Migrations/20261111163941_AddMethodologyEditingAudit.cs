using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NJTruckCharging.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMethodologyEditingAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CreatedByEmail",
                table: "MethodologyVersions",
                type: "nvarchar(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedByUserId",
                table: "MethodologyVersions",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PublishedByEmail",
                table: "MethodologyVersions",
                type: "nvarchar(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PublishedByUserId",
                table: "MethodologyVersions",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Revision",
                table: "MethodologyVersions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "UpdatedByEmail",
                table: "MethodologyVersions",
                type: "nvarchar(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpdatedByUserId",
                table: "MethodologyVersions",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ActorEmail",
                table: "MethodologyActivations",
                type: "nvarchar(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ActorUserId",
                table: "MethodologyActivations",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MethodologyChangeLog",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    VersionId = table.Column<int>(type: "int", nullable: false),
                    VersionName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ActorUserId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ActorEmail = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    RevisionBefore = table.Column<int>(type: "int", nullable: true),
                    RevisionAfter = table.Column<int>(type: "int", nullable: true),
                    RulesHashBefore = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    RulesHashAfter = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    PreviousActiveVersionId = table.Column<int>(type: "int", nullable: true),
                    ChangesJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    BeforeJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AfterJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MethodologyChangeLog", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MethodologyChangeLog_AtUtc",
                table: "MethodologyChangeLog",
                column: "AtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_MethodologyChangeLog_VersionId_AtUtc",
                table: "MethodologyChangeLog",
                columns: new[] { "VersionId", "AtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MethodologyChangeLog");

            migrationBuilder.DropColumn(
                name: "CreatedByEmail",
                table: "MethodologyVersions");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "MethodologyVersions");

            migrationBuilder.DropColumn(
                name: "PublishedByEmail",
                table: "MethodologyVersions");

            migrationBuilder.DropColumn(
                name: "PublishedByUserId",
                table: "MethodologyVersions");

            migrationBuilder.DropColumn(
                name: "Revision",
                table: "MethodologyVersions");

            migrationBuilder.DropColumn(
                name: "UpdatedByEmail",
                table: "MethodologyVersions");

            migrationBuilder.DropColumn(
                name: "UpdatedByUserId",
                table: "MethodologyVersions");

            migrationBuilder.DropColumn(
                name: "ActorEmail",
                table: "MethodologyActivations");

            migrationBuilder.DropColumn(
                name: "ActorUserId",
                table: "MethodologyActivations");
        }
    }
}
