using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VedettaVip.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddThresholdsMaintenanceReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ReminderIncludeWarnings",
                table: "NotificationSettings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "ReminderMinutes",
                table: "NotificationSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsReminder",
                table: "NotificationDeliveries",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "LinkUtilizationThresholdPct",
                table: "MonitoringSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "LossThresholdPct",
                table: "MonitoringSettings",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RttThresholdMs",
                table: "MonitoringSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ThresholdWindowMinutes",
                table: "MonitoringSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "UtilizationThresholdPct",
                table: "MapLinks",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AlertKey",
                table: "Events",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastReminderAt",
                table: "Events",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReminderCount",
                table: "Events",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<double>(
                name: "LossThresholdPct",
                table: "Devices",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RttThresholdMs",
                table: "Devices",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MaintenanceWindows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Scope = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    MapId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: true),
                    Recurrence = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    StartsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EndsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DaysOfWeek = table.Column<int>(type: "integer", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    DurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Notes = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenanceWindows", x => x.Id);
                    table.CheckConstraint("CK_MaintenanceWindows_Schedule", "(\"Recurrence\" = 'Once' AND \"StartsAt\" IS NOT NULL AND \"EndsAt\" IS NOT NULL AND \"EndsAt\" > \"StartsAt\") OR\n(\"Recurrence\" = 'Weekly' AND \"StartTime\" IS NOT NULL AND \"DaysOfWeek\" BETWEEN 1 AND 127\n AND \"DurationMinutes\" BETWEEN 1 AND 10080)");
                    table.CheckConstraint("CK_MaintenanceWindows_Scope", "(\"Scope\" = 'All' AND \"CustomerId\" IS NULL AND \"MapId\" IS NULL AND \"DeviceId\" IS NULL) OR\n(\"Scope\" = 'Customer' AND \"CustomerId\" IS NOT NULL AND \"MapId\" IS NULL AND \"DeviceId\" IS NULL) OR\n(\"Scope\" = 'Map' AND \"MapId\" IS NOT NULL AND \"CustomerId\" IS NULL AND \"DeviceId\" IS NULL) OR\n(\"Scope\" = 'Device' AND \"DeviceId\" IS NOT NULL AND \"CustomerId\" IS NULL AND \"MapId\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_MaintenanceWindows_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MaintenanceWindows_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MaintenanceWindows_Maps_MapId",
                        column: x => x.MapId,
                        principalTable: "Maps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OpenThresholdAlerts",
                columns: table => new
                {
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    LinkId = table.Column<Guid>(type: "uuid", nullable: false),
                    OpenedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Threshold = table.Column<double>(type: "double precision", nullable: false),
                    LastValue = table.Column<double>(type: "double precision", nullable: false),
                    PeakValue = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpenThresholdAlerts", x => new { x.Kind, x.DeviceId, x.LinkId });
                    table.ForeignKey(
                        name: "FK_OpenThresholdAlerts_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "Devices",
                keyColumn: "Id",
                keyValue: new Guid("0198f000-0000-7000-8000-000000000101"),
                columns: new[] { "LossThresholdPct", "RttThresholdMs" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "Devices",
                keyColumn: "Id",
                keyValue: new Guid("0198f000-0000-7000-8000-000000000102"),
                columns: new[] { "LossThresholdPct", "RttThresholdMs" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "Devices",
                keyColumn: "Id",
                keyValue: new Guid("0198f000-0000-7000-8000-000000000103"),
                columns: new[] { "LossThresholdPct", "RttThresholdMs" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "Devices",
                keyColumn: "Id",
                keyValue: new Guid("0198f000-0000-7000-8000-000000000104"),
                columns: new[] { "LossThresholdPct", "RttThresholdMs" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "MapLinks",
                keyColumn: "Id",
                keyValue: new Guid("0198f000-0000-7000-8000-000000000301"),
                column: "UtilizationThresholdPct",
                value: null);

            migrationBuilder.UpdateData(
                table: "MapLinks",
                keyColumn: "Id",
                keyValue: new Guid("0198f000-0000-7000-8000-000000000302"),
                column: "UtilizationThresholdPct",
                value: null);

            migrationBuilder.UpdateData(
                table: "MapLinks",
                keyColumn: "Id",
                keyValue: new Guid("0198f000-0000-7000-8000-000000000303"),
                column: "UtilizationThresholdPct",
                value: null);

            migrationBuilder.UpdateData(
                table: "MapLinks",
                keyColumn: "Id",
                keyValue: new Guid("0198f000-0000-7000-8000-000000000304"),
                column: "UtilizationThresholdPct",
                value: null);

            migrationBuilder.UpdateData(
                table: "MapLinks",
                keyColumn: "Id",
                keyValue: new Guid("0198f000-0000-7000-8000-000000000305"),
                column: "UtilizationThresholdPct",
                value: null);

            migrationBuilder.UpdateData(
                table: "MonitoringSettings",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "LinkUtilizationThresholdPct", "LossThresholdPct", "RttThresholdMs", "ThresholdWindowMinutes" },
                values: new object[] { 80, 5.0, 100, 5 });

            migrationBuilder.UpdateData(
                table: "NotificationSettings",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "ReminderIncludeWarnings", "ReminderMinutes" },
                values: new object[] { false, 0 });

            migrationBuilder.AddCheckConstraint(
                name: "CK_NotificationSettings_Reminder",
                table: "NotificationSettings",
                sql: "\"ReminderMinutes\" BETWEEN 0 AND 10080");

            migrationBuilder.AddCheckConstraint(
                name: "CK_MonitoringSettings_Window",
                table: "MonitoringSettings",
                sql: "\"ThresholdWindowMinutes\" BETWEEN 1 AND 60");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceWindows_CustomerId",
                table: "MaintenanceWindows",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceWindows_DeviceId",
                table: "MaintenanceWindows",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceWindows_MapId",
                table: "MaintenanceWindows",
                column: "MapId");

            migrationBuilder.CreateIndex(
                name: "IX_OpenThresholdAlerts_DeviceId",
                table: "OpenThresholdAlerts",
                column: "DeviceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MaintenanceWindows");

            migrationBuilder.DropTable(
                name: "OpenThresholdAlerts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_NotificationSettings_Reminder",
                table: "NotificationSettings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_MonitoringSettings_Window",
                table: "MonitoringSettings");

            migrationBuilder.DropColumn(
                name: "ReminderIncludeWarnings",
                table: "NotificationSettings");

            migrationBuilder.DropColumn(
                name: "ReminderMinutes",
                table: "NotificationSettings");

            migrationBuilder.DropColumn(
                name: "IsReminder",
                table: "NotificationDeliveries");

            migrationBuilder.DropColumn(
                name: "LinkUtilizationThresholdPct",
                table: "MonitoringSettings");

            migrationBuilder.DropColumn(
                name: "LossThresholdPct",
                table: "MonitoringSettings");

            migrationBuilder.DropColumn(
                name: "RttThresholdMs",
                table: "MonitoringSettings");

            migrationBuilder.DropColumn(
                name: "ThresholdWindowMinutes",
                table: "MonitoringSettings");

            migrationBuilder.DropColumn(
                name: "UtilizationThresholdPct",
                table: "MapLinks");

            migrationBuilder.DropColumn(
                name: "AlertKey",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "LastReminderAt",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "ReminderCount",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "LossThresholdPct",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "RttThresholdMs",
                table: "Devices");
        }
    }
}
