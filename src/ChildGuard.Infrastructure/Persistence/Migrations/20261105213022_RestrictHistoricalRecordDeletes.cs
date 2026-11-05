using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChildGuard.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RestrictHistoricalRecordDeletes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AttendanceRecords_Students_StudentId",
                table: "AttendanceRecords");

            migrationBuilder.DropForeignKey(
                name: "FK_GuardianVerifications_Guardians_GuardianId",
                table: "GuardianVerifications");

            migrationBuilder.DropForeignKey(
                name: "FK_GuardianVerifications_Students_StudentId",
                table: "GuardianVerifications");

            migrationBuilder.DropForeignKey(
                name: "FK_JourneyEvents_Students_StudentId",
                table: "JourneyEvents");

            migrationBuilder.DropForeignKey(
                name: "FK_StudentBusAssignments_Buses_BusId",
                table: "StudentBusAssignments");

            migrationBuilder.AddForeignKey(
                name: "FK_AttendanceRecords_Students_StudentId",
                table: "AttendanceRecords",
                column: "StudentId",
                principalTable: "Students",
                principalColumn: "StudentId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_GuardianVerifications_Guardians_GuardianId",
                table: "GuardianVerifications",
                column: "GuardianId",
                principalTable: "Guardians",
                principalColumn: "GuardianId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_GuardianVerifications_Students_StudentId",
                table: "GuardianVerifications",
                column: "StudentId",
                principalTable: "Students",
                principalColumn: "StudentId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_JourneyEvents_Students_StudentId",
                table: "JourneyEvents",
                column: "StudentId",
                principalTable: "Students",
                principalColumn: "StudentId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StudentBusAssignments_Buses_BusId",
                table: "StudentBusAssignments",
                column: "BusId",
                principalTable: "Buses",
                principalColumn: "BusId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AttendanceRecords_Students_StudentId",
                table: "AttendanceRecords");

            migrationBuilder.DropForeignKey(
                name: "FK_GuardianVerifications_Guardians_GuardianId",
                table: "GuardianVerifications");

            migrationBuilder.DropForeignKey(
                name: "FK_GuardianVerifications_Students_StudentId",
                table: "GuardianVerifications");

            migrationBuilder.DropForeignKey(
                name: "FK_JourneyEvents_Students_StudentId",
                table: "JourneyEvents");

            migrationBuilder.DropForeignKey(
                name: "FK_StudentBusAssignments_Buses_BusId",
                table: "StudentBusAssignments");

            migrationBuilder.AddForeignKey(
                name: "FK_AttendanceRecords_Students_StudentId",
                table: "AttendanceRecords",
                column: "StudentId",
                principalTable: "Students",
                principalColumn: "StudentId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_GuardianVerifications_Guardians_GuardianId",
                table: "GuardianVerifications",
                column: "GuardianId",
                principalTable: "Guardians",
                principalColumn: "GuardianId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_GuardianVerifications_Students_StudentId",
                table: "GuardianVerifications",
                column: "StudentId",
                principalTable: "Students",
                principalColumn: "StudentId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_JourneyEvents_Students_StudentId",
                table: "JourneyEvents",
                column: "StudentId",
                principalTable: "Students",
                principalColumn: "StudentId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_StudentBusAssignments_Buses_BusId",
                table: "StudentBusAssignments",
                column: "BusId",
                principalTable: "Buses",
                principalColumn: "BusId",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
