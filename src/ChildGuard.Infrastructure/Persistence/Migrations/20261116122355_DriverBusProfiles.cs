using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChildGuard.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DriverBusProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AddressCity",
                table: "Drivers",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "AddressCountry",
                table: "Drivers",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "AddressDistrict",
                table: "Drivers",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "AddressLandmark",
                table: "Drivers",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "AddressLine1",
                table: "Drivers",
                type: "varchar(200)",
                maxLength: 200,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "AddressLine2",
                table: "Drivers",
                type: "varchar(200)",
                maxLength: 200,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "AddressLocality",
                table: "Drivers",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "AddressPostalCode",
                table: "Drivers",
                type: "varchar(20)",
                maxLength: 20,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "AddressState",
                table: "Drivers",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "Drivers",
                type: "varchar(200)",
                maxLength: 200,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "EmergencyContactName",
                table: "Drivers",
                type: "varchar(150)",
                maxLength: 150,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "EmergencyContactPhone",
                table: "Drivers",
                type: "varchar(30)",
                maxLength: 30,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "EmployeeCode",
                table: "Drivers",
                type: "varchar(30)",
                maxLength: 30,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateOnly>(
                name: "JoinedOn",
                table: "Drivers",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LicenseCategory",
                table: "Drivers",
                type: "varchar(30)",
                maxLength: 30,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateOnly>(
                name: "LicenseExpiresOn",
                table: "Drivers",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Manufacturer",
                table: "Buses",
                type: "varchar(60)",
                maxLength: 60,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Model",
                table: "Buses",
                type: "varchar(60)",
                maxLength: 60,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "RegistrationNumber",
                table: "Buses",
                type: "varchar(20)",
                maxLength: 20,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Drivers_EmployeeCode",
                table: "Drivers",
                column: "EmployeeCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Buses_RegistrationNumber",
                table: "Buses",
                column: "RegistrationNumber",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Drivers_EmployeeCode",
                table: "Drivers");

            migrationBuilder.DropIndex(
                name: "IX_Buses_RegistrationNumber",
                table: "Buses");

            migrationBuilder.DropColumn(
                name: "AddressCity",
                table: "Drivers");

            migrationBuilder.DropColumn(
                name: "AddressCountry",
                table: "Drivers");

            migrationBuilder.DropColumn(
                name: "AddressDistrict",
                table: "Drivers");

            migrationBuilder.DropColumn(
                name: "AddressLandmark",
                table: "Drivers");

            migrationBuilder.DropColumn(
                name: "AddressLine1",
                table: "Drivers");

            migrationBuilder.DropColumn(
                name: "AddressLine2",
                table: "Drivers");

            migrationBuilder.DropColumn(
                name: "AddressLocality",
                table: "Drivers");

            migrationBuilder.DropColumn(
                name: "AddressPostalCode",
                table: "Drivers");

            migrationBuilder.DropColumn(
                name: "AddressState",
                table: "Drivers");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "Drivers");

            migrationBuilder.DropColumn(
                name: "EmergencyContactName",
                table: "Drivers");

            migrationBuilder.DropColumn(
                name: "EmergencyContactPhone",
                table: "Drivers");

            migrationBuilder.DropColumn(
                name: "EmployeeCode",
                table: "Drivers");

            migrationBuilder.DropColumn(
                name: "JoinedOn",
                table: "Drivers");

            migrationBuilder.DropColumn(
                name: "LicenseCategory",
                table: "Drivers");

            migrationBuilder.DropColumn(
                name: "LicenseExpiresOn",
                table: "Drivers");

            migrationBuilder.DropColumn(
                name: "Manufacturer",
                table: "Buses");

            migrationBuilder.DropColumn(
                name: "Model",
                table: "Buses");

            migrationBuilder.DropColumn(
                name: "RegistrationNumber",
                table: "Buses");
        }
    }
}
