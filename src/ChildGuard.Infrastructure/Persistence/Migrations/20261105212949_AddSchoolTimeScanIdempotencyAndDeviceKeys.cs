using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChildGuard.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// 1. AttendanceRecords.RecordedAtUtc — the UTC instant of each arrival/departure.
    ///    Every existing row was written from DateTime.UtcNow, so its Date/Time ARE the
    ///    UTC instant; the backfill copies them verbatim (no conversion). Converting
    ///    Date/Time to school-local time happens afterwards in AttendanceTimeProjection,
    ///    which only touches rows still equal to their UTC projection (cannot double-shift).
    /// 2. JourneyEvents.ScanId + unique (RfidDeviceId, ScanId) — scanner idempotency key.
    /// 3. RfidDevices.ApiKeyHash/ApiKeyIssuedAt — per-scanner credentials (hash only).
    /// Down restores Date/Time from RecordedAtUtc (UTC, as the old code expects) before
    /// dropping the column, so rolling back leaves attendance exactly as it was.
    /// </summary>
    public partial class AddSchoolTimeScanIdempotencyAndDeviceKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RecordedAtUtc",
                table: "AttendanceRecords",
                type: "datetime(6)",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            // Existing rows: Date + Time already hold the UTC instant (see summary).
            migrationBuilder.Sql(
                "UPDATE AttendanceRecords SET RecordedAtUtc = TIMESTAMP(`Date`, `Time`) " +
                "WHERE RecordedAtUtc = '0001-01-01 00:00:00';");

            migrationBuilder.AddColumn<string>(
                name: "ScanId",
                table: "JourneyEvents",
                type: "varchar(64)",
                maxLength: 64,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            // Create the composite index first: it also serves the RfidDeviceId foreign
            // key, so MySQL allows the old single-column index to be dropped after it.
            migrationBuilder.CreateIndex(
                name: "IX_JourneyEvents_RfidDeviceId_ScanId",
                table: "JourneyEvents",
                columns: new[] { "RfidDeviceId", "ScanId" },
                unique: true);

            migrationBuilder.DropIndex(
                name: "IX_JourneyEvents_RfidDeviceId",
                table: "JourneyEvents");

            migrationBuilder.AddColumn<string>(
                name: "ApiKeyHash",
                table: "RfidDevices",
                type: "char(64)",
                fixedLength: true,
                maxLength: 64,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "ApiKeyIssuedAt",
                table: "RfidDevices",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RfidDevices_ApiKeyHash",
                table: "RfidDevices",
                column: "ApiKeyHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RfidDevices_ApiKeyHash",
                table: "RfidDevices");

            migrationBuilder.DropColumn(
                name: "ApiKeyHash",
                table: "RfidDevices");

            migrationBuilder.DropColumn(
                name: "ApiKeyIssuedAt",
                table: "RfidDevices");

            migrationBuilder.CreateIndex(
                name: "IX_JourneyEvents_RfidDeviceId",
                table: "JourneyEvents",
                column: "RfidDeviceId");

            migrationBuilder.DropIndex(
                name: "IX_JourneyEvents_RfidDeviceId_ScanId",
                table: "JourneyEvents");

            migrationBuilder.DropColumn(
                name: "ScanId",
                table: "JourneyEvents");

            // Put Date/Time back on the UTC projection the previous code reads.
            migrationBuilder.Sql(
                "UPDATE AttendanceRecords SET `Date` = DATE(RecordedAtUtc), `Time` = TIME(RecordedAtUtc);");

            migrationBuilder.DropColumn(
                name: "RecordedAtUtc",
                table: "AttendanceRecords");
        }
    }
}
