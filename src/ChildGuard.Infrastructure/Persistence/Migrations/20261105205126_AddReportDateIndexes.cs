using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChildGuard.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReportDateIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_JourneyEvents_Timestamp",
                table: "JourneyEvents",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_GuardianVerifications_AttemptedAt",
                table: "GuardianVerifications",
                column: "AttemptedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_JourneyEvents_Timestamp",
                table: "JourneyEvents");

            migrationBuilder.DropIndex(
                name: "IX_GuardianVerifications_AttemptedAt",
                table: "GuardianVerifications");
        }
    }
}
