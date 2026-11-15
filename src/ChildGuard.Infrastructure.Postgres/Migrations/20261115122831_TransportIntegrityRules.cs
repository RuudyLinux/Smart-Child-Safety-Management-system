using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ChildGuard.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class TransportIntegrityRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // History table: RESTRICT instead of CASCADE. Done first because MySQL 8 refuses a CASCADE foreign key
            // on StudentId once the stored ActiveStudentKey column (computed from it) exists.
            migrationBuilder.DropForeignKey(
                name: "FK_StudentBusAssignments_Students_StudentId",
                table: "StudentBusAssignments");

            migrationBuilder.AddForeignKey(
                name: "FK_StudentBusAssignments_Students_StudentId",
                table: "StudentBusAssignments",
                column: "StudentId",
                principalTable: "Students",
                principalColumn: "StudentId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.DropIndex(
                name: "IX_StudentBusAssignments_BusId",
                table: "StudentBusAssignments");

            migrationBuilder.AddColumn<DateTime>(
                name: "EndedAt",
                table: "StudentBusAssignments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BusId",
                table: "JourneyEvents",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RouteId",
                table: "JourneyEvents",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StudentBusAssignmentId",
                table: "JourneyEvents",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ActiveStudentKey",
                table: "StudentBusAssignments",
                type: "integer",
                nullable: true,
                computedColumnSql: "CASE WHEN \"Status\" = 1 THEN \"StudentId\" ELSE NULL END",
                stored: true);

            migrationBuilder.CreateTable(
                name: "TransportAuditEntries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AdminId = table.Column<int>(type: "integer", nullable: true),
                    EntityType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    EntityId = table.Column<int>(type: "integer", nullable: false),
                    Action = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Detail = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransportAuditEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StudentBusAssignments_ActiveStudentKey",
                table: "StudentBusAssignments",
                column: "ActiveStudentKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StudentBusAssignments_BusId_Status",
                table: "StudentBusAssignments",
                columns: new[] { "BusId", "Status" });

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

            migrationBuilder.CreateIndex(
                name: "IX_StudentBusAssignments_BusId",
                table: "StudentBusAssignments",
                column: "BusId");

            migrationBuilder.DropForeignKey(
                name: "FK_StudentBusAssignments_Students_StudentId",
                table: "StudentBusAssignments");

            migrationBuilder.AddForeignKey(
                name: "FK_StudentBusAssignments_Students_StudentId",
                table: "StudentBusAssignments",
                column: "StudentId",
                principalTable: "Students",
                principalColumn: "StudentId",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
