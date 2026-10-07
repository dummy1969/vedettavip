using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VedettaVip.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class RouterOsThresholdsAndWatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RouterOsCpuThresholdPct",
                table: "MonitoringSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RouterOsTemperatureThresholdC",
                table: "MonitoringSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CpuThresholdPct",
                table: "Devices",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TemperatureThresholdC",
                table: "Devices",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RouterOsWatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Label = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    Since = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DownReads = table.Column<int>(type: "integer", nullable: false),
                    Detail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RouterOsWatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RouterOsWatches_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "Devices",
                keyColumn: "Id",
                keyValue: new Guid("0198f000-0000-7000-8000-000000000101"),
                columns: new[] { "CpuThresholdPct", "TemperatureThresholdC" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "Devices",
                keyColumn: "Id",
                keyValue: new Guid("0198f000-0000-7000-8000-000000000102"),
                columns: new[] { "CpuThresholdPct", "TemperatureThresholdC" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "Devices",
                keyColumn: "Id",
                keyValue: new Guid("0198f000-0000-7000-8000-000000000103"),
                columns: new[] { "CpuThresholdPct", "TemperatureThresholdC" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "Devices",
                keyColumn: "Id",
                keyValue: new Guid("0198f000-0000-7000-8000-000000000104"),
                columns: new[] { "CpuThresholdPct", "TemperatureThresholdC" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "MonitoringSettings",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "RouterOsCpuThresholdPct", "RouterOsTemperatureThresholdC" },
                values: new object[] { 90, 75 });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Devices_CpuThresholdPct",
                table: "Devices",
                sql: "\"CpuThresholdPct\" IS NULL OR \"CpuThresholdPct\" BETWEEN 0 AND 100");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Devices_TemperatureThresholdC",
                table: "Devices",
                sql: "\"TemperatureThresholdC\" IS NULL OR \"TemperatureThresholdC\" BETWEEN 0 AND 150");

            migrationBuilder.CreateIndex(
                name: "IX_RouterOsWatches_DeviceId_Kind_Key",
                table: "RouterOsWatches",
                columns: new[] { "DeviceId", "Kind", "Key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RouterOsWatches");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Devices_CpuThresholdPct",
                table: "Devices");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Devices_TemperatureThresholdC",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "RouterOsCpuThresholdPct",
                table: "MonitoringSettings");

            migrationBuilder.DropColumn(
                name: "RouterOsTemperatureThresholdC",
                table: "MonitoringSettings");

            migrationBuilder.DropColumn(
                name: "CpuThresholdPct",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "TemperatureThresholdC",
                table: "Devices");
        }
    }
}
