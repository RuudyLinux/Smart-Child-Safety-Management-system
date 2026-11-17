using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ChildGuard.Infrastructure.Postgres.Migrations
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
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AddressCountry",
                table: "Students",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AddressDistrict",
                table: "Students",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AddressLandmark",
                table: "Students",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AddressLine1",
                table: "Students",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AddressLine2",
                table: "Students",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AddressLocality",
                table: "Students",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AddressPostalCode",
                table: "Students",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AddressState",
                table: "Students",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AdmissionNumber",
                table: "Students",
                type: "citext",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Gender",
                table: "Students",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Section",
                table: "Students",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AddressCity",
                table: "Guardians",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AddressCountry",
                table: "Guardians",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AddressDistrict",
                table: "Guardians",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AddressLandmark",
                table: "Guardians",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AddressLine1",
                table: "Guardians",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AddressLine2",
                table: "Guardians",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AddressLocality",
                table: "Guardians",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AddressPostalCode",
                table: "Guardians",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AddressState",
                table: "Guardians",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EnrollmentRequests",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    IdempotencyKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RequestHash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    StudentId = table.Column<int>(type: "integer", nullable: false),
                    AdminId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DuplicateOverrideReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ConfirmedInactiveGuardianIds = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
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
                });

            migrationBuilder.CreateTable(
                name: "RfidCardAssignments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RfidCardId = table.Column<int>(type: "integer", nullable: false),
                    StudentId = table.Column<int>(type: "integer", nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EndedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RecordedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AssignedByAdminId = table.Column<int>(type: "integer", nullable: true),
                    IsMigratedSnapshot = table.Column<bool>(type: "boolean", nullable: false)
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
                });

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
                "INSERT INTO \"RfidCardAssignments\" (\"RfidCardId\", \"StudentId\", \"AssignedAt\", \"EndedAt\", \"RecordedAt\", \"AssignedByAdminId\", \"IsMigratedSnapshot\") " +
                "SELECT \"RfidCardId\", \"StudentId\", \"AssignedAt\", NULL, now(), NULL, TRUE FROM \"RfidCards\" WHERE \"StudentId\" IS NOT NULL;");
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
