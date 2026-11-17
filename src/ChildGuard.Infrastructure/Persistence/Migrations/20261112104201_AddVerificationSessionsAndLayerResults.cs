using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChildGuard.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVerificationSessionsAndLayerResults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AuthorisationResult",
                table: "GuardianVerifications",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BestReferenceId",
                table: "GuardianVerifications",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CaptureSource",
                table: "GuardianVerifications",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FaceDetectionResult",
                table: "GuardianVerifications",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "IdentityResult",
                table: "GuardianVerifications",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LayerNotes",
                table: "GuardianVerifications",
                type: "varchar(500)",
                maxLength: 500,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "LivenessResult",
                table: "GuardianVerifications",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "LivenessScore",
                table: "GuardianVerifications",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "QualityResult",
                table: "GuardianVerifications",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SessionId",
                table: "GuardianVerifications",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "VerificationSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    StudentId = table.Column<int>(type: "int", nullable: false),
                    CreatedByAdminId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    IdentifiedGuardianId = table.Column<int>(type: "int", nullable: true),
                    IdentifiedSource = table.Column<int>(type: "int", nullable: true),
                    IdentifiedScore = table.Column<double>(type: "double", nullable: true),
                    ChallengeTokenHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ChallengeSteps = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ChallengeIssuedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ChallengeExpiresAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ChallengeConsumedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    FailedLiveAttempts = table.Column<int>(type: "int", nullable: false),
                    NeutralFrameHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    VerificationId = table.Column<int>(type: "int", nullable: true),
                    Version = table.Column<uint>(type: "int unsigned", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VerificationSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VerificationSessions_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "StudentId",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_VerificationSessions_NeutralFrameHash",
                table: "VerificationSessions",
                column: "NeutralFrameHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VerificationSessions_StudentId_State",
                table: "VerificationSessions",
                columns: new[] { "StudentId", "State" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VerificationSessions");

            migrationBuilder.DropColumn(
                name: "AuthorisationResult",
                table: "GuardianVerifications");

            migrationBuilder.DropColumn(
                name: "BestReferenceId",
                table: "GuardianVerifications");

            migrationBuilder.DropColumn(
                name: "CaptureSource",
                table: "GuardianVerifications");

            migrationBuilder.DropColumn(
                name: "FaceDetectionResult",
                table: "GuardianVerifications");

            migrationBuilder.DropColumn(
                name: "IdentityResult",
                table: "GuardianVerifications");

            migrationBuilder.DropColumn(
                name: "LayerNotes",
                table: "GuardianVerifications");

            migrationBuilder.DropColumn(
                name: "LivenessResult",
                table: "GuardianVerifications");

            migrationBuilder.DropColumn(
                name: "LivenessScore",
                table: "GuardianVerifications");

            migrationBuilder.DropColumn(
                name: "QualityResult",
                table: "GuardianVerifications");

            migrationBuilder.DropColumn(
                name: "SessionId",
                table: "GuardianVerifications");
        }
    }
}
