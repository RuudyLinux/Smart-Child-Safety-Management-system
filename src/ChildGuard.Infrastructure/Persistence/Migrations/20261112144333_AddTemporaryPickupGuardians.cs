using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChildGuard.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTemporaryPickupGuardians : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TemporaryPickupAuditEntries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    AuthorizationId = table.Column<int>(type: "int", nullable: false),
                    StudentId = table.Column<int>(type: "int", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Action = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ActorRole = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ActorId = table.Column<int>(type: "int", nullable: true),
                    Detail = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TemporaryPickupAuditEntries", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "TemporaryPickupAuthorizations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    StudentId = table.Column<int>(type: "int", nullable: false),
                    RequestedByGuardianId = table.Column<int>(type: "int", nullable: false),
                    FullName = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Phone = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Relationship = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IdentityNote = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Notes = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ValidFromUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ValidUntilUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    DecidedByAdminId = table.Column<int>(type: "int", nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    DecisionNote = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CancelledAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CompletedByAdminId = table.Column<int>(type: "int", nullable: true),
                    HandoverFaceScore = table.Column<double>(type: "double", nullable: true),
                    JourneyEventId = table.Column<int>(type: "int", nullable: true),
                    QrNonce = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FaceEmbeddingJson = table.Column<string>(type: "LONGTEXT", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FaceModel = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Version = table.Column<uint>(type: "int unsigned", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TemporaryPickupAuthorizations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TemporaryPickupAuthorizations_Admins_CompletedByAdminId",
                        column: x => x.CompletedByAdminId,
                        principalTable: "Admins",
                        principalColumn: "AdminId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TemporaryPickupAuthorizations_Admins_DecidedByAdminId",
                        column: x => x.DecidedByAdminId,
                        principalTable: "Admins",
                        principalColumn: "AdminId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TemporaryPickupAuthorizations_Guardians_RequestedByGuardianId",
                        column: x => x.RequestedByGuardianId,
                        principalTable: "Guardians",
                        principalColumn: "GuardianId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TemporaryPickupAuthorizations_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "StudentId",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "TemporaryPickupPhotos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    TemporaryPickupAuthorizationId = table.Column<int>(type: "int", nullable: false),
                    ContentType = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Data = table.Column<byte[]>(type: "LONGBLOB", nullable: false),
                    ByteSize = table.Column<int>(type: "int", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TemporaryPickupPhotos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TemporaryPickupPhotos_TemporaryPickupAuthorizations_Temporar~",
                        column: x => x.TemporaryPickupAuthorizationId,
                        principalTable: "TemporaryPickupAuthorizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_TemporaryPickupAuditEntries_AuthorizationId_OccurredAt",
                table: "TemporaryPickupAuditEntries",
                columns: new[] { "AuthorizationId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TemporaryPickupAuditEntries_StudentId",
                table: "TemporaryPickupAuditEntries",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_TemporaryPickupAuthorizations_CompletedByAdminId",
                table: "TemporaryPickupAuthorizations",
                column: "CompletedByAdminId");

            migrationBuilder.CreateIndex(
                name: "IX_TemporaryPickupAuthorizations_DecidedByAdminId",
                table: "TemporaryPickupAuthorizations",
                column: "DecidedByAdminId");

            migrationBuilder.CreateIndex(
                name: "IX_TemporaryPickupAuthorizations_RequestedByGuardianId",
                table: "TemporaryPickupAuthorizations",
                column: "RequestedByGuardianId");

            migrationBuilder.CreateIndex(
                name: "IX_TemporaryPickupAuthorizations_Status_ValidUntilUtc",
                table: "TemporaryPickupAuthorizations",
                columns: new[] { "Status", "ValidUntilUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TemporaryPickupAuthorizations_StudentId_ValidUntilUtc",
                table: "TemporaryPickupAuthorizations",
                columns: new[] { "StudentId", "ValidUntilUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TemporaryPickupPhotos_TemporaryPickupAuthorizationId",
                table: "TemporaryPickupPhotos",
                column: "TemporaryPickupAuthorizationId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TemporaryPickupAuditEntries");

            migrationBuilder.DropTable(
                name: "TemporaryPickupPhotos");

            migrationBuilder.DropTable(
                name: "TemporaryPickupAuthorizations");
        }
    }
}
