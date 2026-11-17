using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChildGuard.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGuardianOtpLoginFlow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PasswordResetRequired",
                table: "Guardians",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "GuardianOtpChallenges",
                columns: table => new
                {
                    GuardianOtpChallengeId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ChallengeToken = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    GuardianId = table.Column<int>(type: "int", nullable: false),
                    CodeHash = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    FailedAttempts = table.Column<int>(type: "int", nullable: false),
                    ResendCount = table.Column<int>(type: "int", nullable: false),
                    LastSentAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    VerifiedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    IsConsumed = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GuardianOtpChallenges", x => x.GuardianOtpChallengeId);
                    table.ForeignKey(
                        name: "FK_GuardianOtpChallenges_Guardians_GuardianId",
                        column: x => x.GuardianId,
                        principalTable: "Guardians",
                        principalColumn: "GuardianId",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            // The contact number becomes the Guardian login identifier, so existing rows
            // are normalized to the same form the application writes and looks up
            // (digits only, keeping a leading '+') before the unique index is added.
            migrationBuilder.Sql("UPDATE Guardians SET Phone = NULL WHERE Phone IS NOT NULL AND TRIM(Phone) = '';");
            migrationBuilder.Sql(
                "UPDATE Guardians SET Phone = CONCAT(" +
                "CASE WHEN TRIM(Phone) LIKE '+%' THEN '+' ELSE '' END, " +
                "REGEXP_REPLACE(Phone, '[^0-9]', '')) WHERE Phone IS NOT NULL;");
            migrationBuilder.Sql("UPDATE Guardians SET Phone = NULL WHERE Phone IN ('', '+');");

            migrationBuilder.CreateIndex(
                name: "IX_Guardians_Phone",
                table: "Guardians",
                column: "Phone",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GuardianOtpChallenges_ChallengeToken",
                table: "GuardianOtpChallenges",
                column: "ChallengeToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GuardianOtpChallenges_GuardianId_IsConsumed",
                table: "GuardianOtpChallenges",
                columns: new[] { "GuardianId", "IsConsumed" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GuardianOtpChallenges");

            migrationBuilder.DropIndex(
                name: "IX_Guardians_Phone",
                table: "Guardians");

            migrationBuilder.DropColumn(
                name: "PasswordResetRequired",
                table: "Guardians");
        }
    }
}
