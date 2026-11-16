using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChildGuard.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnrollmentAndAddresses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AddressCity",
                table: "Students",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "AddressCountry",
                table: "Students",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "AddressDistrict",
                table: "Students",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "AddressLandmark",
                table: "Students",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "AddressLine1",
                table: "Students",
                type: "varchar(200)",
                maxLength: 200,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "AddressLine2",
                table: "Students",
                type: "varchar(200)",
                maxLength: 200,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "AddressLocality",
                table: "Students",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "AddressPostalCode",
                table: "Students",
                type: "varchar(20)",
                maxLength: 20,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "AddressState",
                table: "Students",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "AdmissionNumber",
                table: "Students",
                type: "varchar(50)",
                maxLength: 50,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "Gender",
                table: "Students",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Section",
                table: "Students",
                type: "varchar(20)",
                maxLength: 20,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "AddressCity",
                table: "Guardians",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "AddressCountry",
                table: "Guardians",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "AddressDistrict",
                table: "Guardians",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "AddressLandmark",
                table: "Guardians",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "AddressLine1",
                table: "Guardians",
                type: "varchar(200)",
                maxLength: 200,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "AddressLine2",
                table: "Guardians",
                type: "varchar(200)",
                maxLength: 200,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "AddressLocality",
                table: "Guardians",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "AddressPostalCode",
                table: "Guardians",
                type: "varchar(20)",
                maxLength: 20,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "AddressState",
                table: "Guardians",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "EnrollmentRequests",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    IdempotencyKey = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RequestHash = table.Column<string>(type: "char(64)", fixedLength: true, maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StudentId = table.Column<int>(type: "int", nullable: false),
                    AdminId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    DuplicateOverrideReason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConfirmedInactiveGuardianIds = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnrollmentRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EnrollmentRequests_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "StudentId",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "RfidCardAssignments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    RfidCardId = table.Column<int>(type: "int", nullable: false),
                    StudentId = table.Column<int>(type: "int", nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    EndedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    RecordedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    AssignedByAdminId = table.Column<int>(type: "int", nullable: true),
                    IsMigratedSnapshot = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RfidCardAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RfidCardAssignments_RfidCards_RfidCardId",
                        column: x => x.RfidCardId,
                        principalTable: "RfidCards",
                        principalColumn: "RfidCardId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RfidCardAssignments_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "StudentId",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Students_AdmissionNumber",
                table: "Students",
                column: "AdmissionNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EnrollmentRequests_IdempotencyKey",
                table: "EnrollmentRequests",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EnrollmentRequests_StudentId",
                table: "EnrollmentRequests",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_RfidCardAssignments_RfidCardId_RecordedAt",
                table: "RfidCardAssignments",
                columns: new[] { "RfidCardId", "RecordedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RfidCardAssignments_StudentId",
                table: "RfidCardAssignments",
                column: "StudentId");

            // Existing card links become history rows flagged as migrated snapshots. AssignedAt is the card's own
            // recorded value (NULL stays NULL, never estimated); RecordedAt is when this migration ran.
            migrationBuilder.Sql(
                "INSERT INTO RfidCardAssignments (RfidCardId, StudentId, AssignedAt, EndedAt, RecordedAt, AssignedByAdminId, IsMigratedSnapshot) " +
                "SELECT RfidCardId, StudentId, AssignedAt, NULL, UTC_TIMESTAMP(6), NULL, 1 FROM RfidCards WHERE StudentId IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EnrollmentRequests");

            migrationBuilder.DropTable(
                name: "RfidCardAssignments");

            migrationBuilder.DropIndex(
                name: "IX_Students_AdmissionNumber",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "AddressCity",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "AddressCountry",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "AddressDistrict",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "AddressLandmark",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "AddressLine1",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "AddressLine2",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "AddressLocality",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "AddressPostalCode",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "AddressState",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "AdmissionNumber",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "Gender",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "Section",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "AddressCity",
                table: "Guardians");

            migrationBuilder.DropColumn(
                name: "AddressCountry",
                table: "Guardians");

            migrationBuilder.DropColumn(
                name: "AddressDistrict",
                table: "Guardians");

            migrationBuilder.DropColumn(
                name: "AddressLandmark",
                table: "Guardians");

            migrationBuilder.DropColumn(
                name: "AddressLine1",
                table: "Guardians");

            migrationBuilder.DropColumn(
                name: "AddressLine2",
                table: "Guardians");

            migrationBuilder.DropColumn(
                name: "AddressLocality",
                table: "Guardians");

            migrationBuilder.DropColumn(
                name: "AddressPostalCode",
                table: "Guardians");

            migrationBuilder.DropColumn(
                name: "AddressState",
                table: "Guardians");
        }
    }
}
