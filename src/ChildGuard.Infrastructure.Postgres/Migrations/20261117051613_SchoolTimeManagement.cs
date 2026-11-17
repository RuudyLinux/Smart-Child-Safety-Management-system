using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ChildGuard.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class SchoolTimeManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SchoolCalendarDays",
                columns: table => new
                {
                    SchoolCalendarDayId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    DayType = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    LateArrivalAfter = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    DismissalTime = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SchoolCalendarDays", x => x.SchoolCalendarDayId);
                });

            migrationBuilder.CreateTable(
                name: "SchoolTimingSettings",
                columns: table => new
                {
                    SchoolTimingSettingsId = table.Column<int>(type: "integer", nullable: false),
                    WorkingDays = table.Column<int>(type: "integer", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    LateArrivalAfter = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    MissingCheckInAfter = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    DismissalTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    PickupGraceMinutes = table.Column<int>(type: "integer", nullable: false),
                    AttendanceWindowDays = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedByAdminId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SchoolTimingSettings", x => x.SchoolTimingSettingsId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SchoolCalendarDays_Date",
                table: "SchoolCalendarDays",
                column: "Date",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SchoolCalendarDays");

            migrationBuilder.DropTable(
                name: "SchoolTimingSettings");
        }
    }
}
