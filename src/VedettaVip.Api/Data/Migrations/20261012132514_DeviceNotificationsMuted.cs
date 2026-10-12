using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VedettaVip.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class DeviceNotificationsMuted : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "NotificationsMuted",
                table: "Devices",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.UpdateData(
                table: "Devices",
                keyColumn: "Id",
                keyValue: new Guid("0198f000-0000-7000-8000-000000000101"),
                column: "NotificationsMuted",
                value: false);

            migrationBuilder.UpdateData(
                table: "Devices",
                keyColumn: "Id",
                keyValue: new Guid("0198f000-0000-7000-8000-000000000102"),
                column: "NotificationsMuted",
                value: false);

            migrationBuilder.UpdateData(
                table: "Devices",
                keyColumn: "Id",
                keyValue: new Guid("0198f000-0000-7000-8000-000000000103"),
                column: "NotificationsMuted",
                value: false);

            migrationBuilder.UpdateData(
                table: "Devices",
                keyColumn: "Id",
                keyValue: new Guid("0198f000-0000-7000-8000-000000000104"),
                column: "NotificationsMuted",
                value: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NotificationsMuted",
                table: "Devices");
        }
    }
}
