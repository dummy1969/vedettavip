using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VedettaVip.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDashboardSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DashboardEventHours",
                table: "MonitoringSettings",
                type: "integer",
                nullable: false,
                defaultValue: 4);

            migrationBuilder.UpdateData(
                table: "MonitoringSettings",
                keyColumn: "Id",
                keyValue: 1,
                column: "DashboardEventHours",
                value: 4);

            migrationBuilder.AddCheckConstraint(
                name: "CK_MonitoringSettings_DashboardHours",
                table: "MonitoringSettings",
                sql: "\"DashboardEventHours\" BETWEEN 1 AND 168");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_MonitoringSettings_DashboardHours",
                table: "MonitoringSettings");

            migrationBuilder.DropColumn(
                name: "DashboardEventHours",
                table: "MonitoringSettings");
        }
    }
}
