using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NJTruckCharging.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TuneSiteQueryIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CandidateSites_Score",
                table: "CandidateSites");

            migrationBuilder.CreateIndex(
                name: "IX_CandidateSites_Score_MilesToExit",
                table: "CandidateSites",
                columns: new[] { "Score", "MilesToExit" },
                descending: new[] { true, false });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CandidateSites_Score_MilesToExit",
                table: "CandidateSites");

            migrationBuilder.CreateIndex(
                name: "IX_CandidateSites_Score",
                table: "CandidateSites",
                column: "Score");
        }
    }
}
