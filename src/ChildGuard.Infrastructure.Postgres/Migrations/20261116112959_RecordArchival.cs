using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChildGuard.Infrastructure.Postgres.Migrations
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
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ArchivedAt",
                table: "Students",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ArchivedByAdminId",
                table: "Students",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ArchiveReason",
                table: "Guardians",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ArchivedAt",
                table: "Guardians",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ArchivedByAdminId",
                table: "Guardians",
                type: "integer",
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
