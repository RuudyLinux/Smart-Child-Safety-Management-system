using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChildGuard.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RecordArchival : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ArchiveReason",
                table: "Students",
                type: "varchar(500)",
                maxLength: 500,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "ArchivedAt",
                table: "Students",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ArchivedByAdminId",
                table: "Students",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ArchiveReason",
                table: "Guardians",
                type: "varchar(500)",
                maxLength: 500,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "ArchivedAt",
                table: "Guardians",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ArchivedByAdminId",
                table: "Guardians",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ArchiveReason",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "ArchivedAt",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "ArchivedByAdminId",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "ArchiveReason",
                table: "Guardians");

            migrationBuilder.DropColumn(
                name: "ArchivedAt",
                table: "Guardians");

            migrationBuilder.DropColumn(
                name: "ArchivedByAdminId",
                table: "Guardians");
        }
    }
}
