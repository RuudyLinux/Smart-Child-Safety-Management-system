using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChildGuard.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RouteStops : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DropStopId",
                table: "StudentBusAssignments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PickupStopId",
                table: "StudentBusAssignments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Destination",
                table: "Routes",
                type: "varchar(150)",
                maxLength: 150,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "StartPoint",
                table: "Routes",
                type: "varchar(150)",
                maxLength: 150,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "RouteStops",
                columns: table => new
                {
                    RouteStopId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    RouteId = table.Column<int>(type: "int", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Address = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PickupTime = table.Column<TimeOnly>(type: "time(6)", nullable: true),
                    DropTime = table.Column<TimeOnly>(type: "time(6)", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RouteStops", x => x.RouteStopId);
                    table.ForeignKey(
                        name: "FK_RouteStops_Routes_RouteId",
                        column: x => x.RouteId,
                        principalTable: "Routes",
                        principalColumn: "RouteId",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_StudentBusAssignments_DropStopId",
                table: "StudentBusAssignments",
                column: "DropStopId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentBusAssignments_PickupStopId",
                table: "StudentBusAssignments",
                column: "PickupStopId");

            migrationBuilder.CreateIndex(
                name: "IX_RouteStops_RouteId_Sequence",
                table: "RouteStops",
                columns: new[] { "RouteId", "Sequence" });

            migrationBuilder.AddForeignKey(
                name: "FK_StudentBusAssignments_RouteStops_DropStopId",
                table: "StudentBusAssignments",
                column: "DropStopId",
                principalTable: "RouteStops",
                principalColumn: "RouteStopId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StudentBusAssignments_RouteStops_PickupStopId",
                table: "StudentBusAssignments",
                column: "PickupStopId",
                principalTable: "RouteStops",
                principalColumn: "RouteStopId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StudentBusAssignments_RouteStops_DropStopId",
                table: "StudentBusAssignments");

            migrationBuilder.DropForeignKey(
                name: "FK_StudentBusAssignments_RouteStops_PickupStopId",
                table: "StudentBusAssignments");

            migrationBuilder.DropTable(
                name: "RouteStops");

            migrationBuilder.DropIndex(
                name: "IX_StudentBusAssignments_DropStopId",
                table: "StudentBusAssignments");

            migrationBuilder.DropIndex(
                name: "IX_StudentBusAssignments_PickupStopId",
                table: "StudentBusAssignments");

            migrationBuilder.DropColumn(
                name: "DropStopId",
                table: "StudentBusAssignments");

            migrationBuilder.DropColumn(
                name: "PickupStopId",
                table: "StudentBusAssignments");

            migrationBuilder.DropColumn(
                name: "Destination",
                table: "Routes");

            migrationBuilder.DropColumn(
                name: "StartPoint",
                table: "Routes");
        }
    }
}
