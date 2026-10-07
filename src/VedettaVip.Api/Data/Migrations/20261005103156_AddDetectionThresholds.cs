using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VedettaVip.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDetectionThresholds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DownAfterFailures",
                table: "Devices",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SnmpDegradedAfterFailures",
                table: "Devices",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UpAfterSuccesses",
                table: "Devices",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MonitoringSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    DownAfterFailures = table.Column<int>(type: "integer", nullable: false),
                    UpAfterSuccesses = table.Column<int>(type: "integer", nullable: false),
                    SnmpDegradedAfterFailures = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonitoringSettings", x => x.Id);
                    table.CheckConstraint("CK_MonitoringSettings_Singleton", "\"Id\" = 1");
                    table.CheckConstraint("CK_MonitoringSettings_Thresholds", "\"DownAfterFailures\" BETWEEN 1 AND 100 AND \"UpAfterSuccesses\" BETWEEN 1 AND 100 AND \"SnmpDegradedAfterFailures\" BETWEEN 1 AND 100");
                });

            migrationBuilder.UpdateData(
                table: "Devices",
                keyColumn: "Id",
                keyValue: new Guid("0198f000-0000-7000-8000-000000000101"),
                columns: new[] { "DownAfterFailures", "SnmpDegradedAfterFailures", "UpAfterSuccesses" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "Devices",
                keyColumn: "Id",
                keyValue: new Guid("0198f000-0000-7000-8000-000000000102"),
                columns: new[] { "DownAfterFailures", "SnmpDegradedAfterFailures", "UpAfterSuccesses" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "Devices",
                keyColumn: "Id",
                keyValue: new Guid("0198f000-0000-7000-8000-000000000103"),
                columns: new[] { "DownAfterFailures", "SnmpDegradedAfterFailures", "UpAfterSuccesses" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "Devices",
                keyColumn: "Id",
                keyValue: new Guid("0198f000-0000-7000-8000-000000000104"),
                columns: new[] { "DownAfterFailures", "SnmpDegradedAfterFailures", "UpAfterSuccesses" },
                values: new object[] { null, null, null });

            migrationBuilder.InsertData(
                table: "MonitoringSettings",
                columns: new[] { "Id", "DownAfterFailures", "SnmpDegradedAfterFailures", "UpAfterSuccesses" },
                values: new object[] { 1, 3, 2, 2 });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Devices_DownAfterFailures",
                table: "Devices",
                sql: "\"DownAfterFailures\" IS NULL OR \"DownAfterFailures\" BETWEEN 1 AND 100");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Devices_SnmpDegradedAfterFailures",
                table: "Devices",
                sql: "\"SnmpDegradedAfterFailures\" IS NULL OR \"SnmpDegradedAfterFailures\" BETWEEN 1 AND 100");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Devices_UpAfterSuccesses",
                table: "Devices",
                sql: "\"UpAfterSuccesses\" IS NULL OR \"UpAfterSuccesses\" BETWEEN 1 AND 100");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MonitoringSettings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Devices_DownAfterFailures",
                table: "Devices");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Devices_SnmpDegradedAfterFailures",
                table: "Devices");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Devices_UpAfterSuccesses",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "DownAfterFailures",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "SnmpDegradedAfterFailures",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "UpAfterSuccesses",
                table: "Devices");
        }
    }
}
