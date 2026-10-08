using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VedettaVip.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceVendorAndWinBox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "WinBoxLinuxPath",
                table: "MonitoringSettings",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WinBoxWindowsPath",
                table: "MonitoringSettings",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Vendor",
                table: "Devices",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Generic");

            // L'API RouterOS esiste solo sui MikroTik (seed compreso): da qui "Apri con WinBox"
            migrationBuilder.Sql("""UPDATE "Devices" SET "Vendor" = 'MikroTik' WHERE "RouterOsApiEnabled";""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WinBoxLinuxPath",
                table: "MonitoringSettings");

            migrationBuilder.DropColumn(
                name: "WinBoxWindowsPath",
                table: "MonitoringSettings");

            migrationBuilder.DropColumn(
                name: "Vendor",
                table: "Devices");
        }
    }
}
