using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VedettaVip.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class WinBoxPort : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "WinBoxDefaultPort",
                table: "MonitoringSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WinBoxPort",
                table: "Devices",
                type: "integer",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Devices",
                keyColumn: "Id",
                keyValue: new Guid("0198f000-0000-7000-8000-000000000101"),
                column: "WinBoxPort",
                value: null);

            migrationBuilder.UpdateData(
                table: "Devices",
                keyColumn: "Id",
                keyValue: new Guid("0198f000-0000-7000-8000-000000000102"),
                column: "WinBoxPort",
                value: null);

            migrationBuilder.UpdateData(
                table: "Devices",
                keyColumn: "Id",
                keyValue: new Guid("0198f000-0000-7000-8000-000000000103"),
                column: "WinBoxPort",
                value: null);

            migrationBuilder.UpdateData(
                table: "Devices",
                keyColumn: "Id",
                keyValue: new Guid("0198f000-0000-7000-8000-000000000104"),
                column: "WinBoxPort",
                value: null);

            migrationBuilder.UpdateData(
                table: "MonitoringSettings",
                keyColumn: "Id",
                keyValue: 1,
                column: "WinBoxDefaultPort",
                value: null);

            migrationBuilder.AddCheckConstraint(
                name: "CK_MonitoringSettings_WinBoxPort",
                table: "MonitoringSettings",
                sql: "\"WinBoxDefaultPort\" IS NULL OR \"WinBoxDefaultPort\" BETWEEN 1 AND 65535");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Devices_WinBoxPort",
                table: "Devices",
                sql: "\"WinBoxPort\" IS NULL OR \"WinBoxPort\" BETWEEN 1 AND 65535");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_MonitoringSettings_WinBoxPort",
                table: "MonitoringSettings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Devices_WinBoxPort",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "WinBoxDefaultPort",
                table: "MonitoringSettings");

            migrationBuilder.DropColumn(
                name: "WinBoxPort",
                table: "Devices");
        }
    }
}
