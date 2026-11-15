using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChildGuard.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TransportIntegrityRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "EndedAt",
                table: "StudentBusAssignments",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BusId",
                table: "JourneyEvents",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RouteId",
                table: "JourneyEvents",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StudentBusAssignmentId",
                table: "JourneyEvents",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ActiveStudentKey",
                table: "StudentBusAssignments",
                type: "int",
                nullable: true,
                computedColumnSql: "CASE WHEN `Status` = 1 THEN `StudentId` ELSE NULL END",
                stored: true);

            migrationBuilder.CreateTable(
                name: "TransportAuditEntries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    OccurredAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    AdminId = table.Column<int>(type: "int", nullable: true),
                    EntityType = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EntityId = table.Column<int>(type: "int", nullable: false),
                    Action = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Detail = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransportAuditEntries", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_StudentBusAssignments_ActiveStudentKey",
                table: "StudentBusAssignments",
                column: "ActiveStudentKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StudentBusAssignments_BusId_Status",
                table: "StudentBusAssignments",
                columns: new[] { "BusId", "Status" });

            // Dropped only now: MySQL keeps an index for the BusId foreign key, and (BusId, Status) takes over that role.
            migrationBuilder.DropIndex(
                name: "IX_StudentBusAssignments_BusId",
                table: "StudentBusAssignments");

            migrationBuilder.CreateIndex(
                name: "IX_Routes_RouteName",
                table: "Routes",
                column: "RouteName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JourneyEvents_BusId",
                table: "JourneyEvents",
                column: "BusId");

            migrationBuilder.CreateIndex(
                name: "IX_JourneyEvents_RouteId",
                table: "JourneyEvents",
                column: "RouteId");

            migrationBuilder.CreateIndex(
                name: "IX_JourneyEvents_StudentBusAssignmentId",
                table: "JourneyEvents",
                column: "StudentBusAssignmentId");

            migrationBuilder.CreateIndex(
                name: "IX_TransportAuditEntries_EntityType_EntityId",
                table: "TransportAuditEntries",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_TransportAuditEntries_OccurredAt",
                table: "TransportAuditEntries",
                column: "OccurredAt");

            migrationBuilder.AddForeignKey(
                name: "FK_JourneyEvents_Buses_BusId",
                table: "JourneyEvents",
                column: "BusId",
                principalTable: "Buses",
                principalColumn: "BusId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_JourneyEvents_Routes_RouteId",
                table: "JourneyEvents",
                column: "RouteId",
                principalTable: "Routes",
                principalColumn: "RouteId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_JourneyEvents_StudentBusAssignments_StudentBusAssignmentId",
                table: "JourneyEvents",
                column: "StudentBusAssignmentId",
                principalTable: "StudentBusAssignments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_JourneyEvents_Buses_BusId",
                table: "JourneyEvents");

            migrationBuilder.DropForeignKey(
                name: "FK_JourneyEvents_Routes_RouteId",
                table: "JourneyEvents");

            migrationBuilder.DropForeignKey(
                name: "FK_JourneyEvents_StudentBusAssignments_StudentBusAssignmentId",
                table: "JourneyEvents");

            migrationBuilder.DropTable(
                name: "TransportAuditEntries");

            migrationBuilder.DropIndex(
                name: "IX_StudentBusAssignments_ActiveStudentKey",
                table: "StudentBusAssignments");

            migrationBuilder.CreateIndex(
                name: "IX_StudentBusAssignments_BusId",
                table: "StudentBusAssignments",
                column: "BusId");

            migrationBuilder.DropIndex(
                name: "IX_StudentBusAssignments_BusId_Status",
                table: "StudentBusAssignments");

            migrationBuilder.DropIndex(
                name: "IX_Routes_RouteName",
                table: "Routes");

            migrationBuilder.DropIndex(
                name: "IX_JourneyEvents_BusId",
                table: "JourneyEvents");

            migrationBuilder.DropIndex(
                name: "IX_JourneyEvents_RouteId",
                table: "JourneyEvents");

            migrationBuilder.DropIndex(
                name: "IX_JourneyEvents_StudentBusAssignmentId",
                table: "JourneyEvents");

            migrationBuilder.DropColumn(
                name: "ActiveStudentKey",
                table: "StudentBusAssignments");

            migrationBuilder.DropColumn(
                name: "EndedAt",
                table: "StudentBusAssignments");

            migrationBuilder.DropColumn(
                name: "BusId",
                table: "JourneyEvents");

            migrationBuilder.DropColumn(
                name: "RouteId",
                table: "JourneyEvents");

            migrationBuilder.DropColumn(
                name: "StudentBusAssignmentId",
                table: "JourneyEvents");

        }
    }
}
