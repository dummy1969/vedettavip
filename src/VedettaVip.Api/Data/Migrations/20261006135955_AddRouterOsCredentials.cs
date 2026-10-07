using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VedettaVip.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRouterOsCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DefaultRouterOsCredentialId",
                table: "MonitoringSettings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RouterOsCredentialId",
                table: "Devices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RouterOsCredentialId",
                table: "Customers",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RouterOsCredentials",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Username = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PasswordProtected = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    UseTls = table.Column<bool>(type: "boolean", nullable: false),
                    Port = table.Column<int>(type: "integer", nullable: false),
                    VerifyCertificate = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RouterOsCredentials", x => x.Id);
                    table.CheckConstraint("CK_RouterOsCredentials_Port", "\"Port\" BETWEEN 1 AND 65535");
                });

            migrationBuilder.UpdateData(
                table: "Devices",
                keyColumn: "Id",
                keyValue: new Guid("0198f000-0000-7000-8000-000000000101"),
                column: "RouterOsCredentialId",
                value: null);

            migrationBuilder.UpdateData(
                table: "Devices",
                keyColumn: "Id",
                keyValue: new Guid("0198f000-0000-7000-8000-000000000102"),
                column: "RouterOsCredentialId",
                value: null);

            migrationBuilder.UpdateData(
                table: "Devices",
                keyColumn: "Id",
                keyValue: new Guid("0198f000-0000-7000-8000-000000000103"),
                column: "RouterOsCredentialId",
                value: null);

            migrationBuilder.UpdateData(
                table: "Devices",
                keyColumn: "Id",
                keyValue: new Guid("0198f000-0000-7000-8000-000000000104"),
                column: "RouterOsCredentialId",
                value: null);

            migrationBuilder.UpdateData(
                table: "MonitoringSettings",
                keyColumn: "Id",
                keyValue: 1,
                column: "DefaultRouterOsCredentialId",
                value: null);

            migrationBuilder.CreateIndex(
                name: "IX_MonitoringSettings_DefaultRouterOsCredentialId",
                table: "MonitoringSettings",
                column: "DefaultRouterOsCredentialId");

            migrationBuilder.CreateIndex(
                name: "IX_Devices_RouterOsCredentialId",
                table: "Devices",
                column: "RouterOsCredentialId");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_RouterOsCredentialId",
                table: "Customers",
                column: "RouterOsCredentialId");

            migrationBuilder.CreateIndex(
                name: "IX_RouterOsCredentials_Name",
                table: "RouterOsCredentials",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Customers_RouterOsCredentials_RouterOsCredentialId",
                table: "Customers",
                column: "RouterOsCredentialId",
                principalTable: "RouterOsCredentials",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Devices_RouterOsCredentials_RouterOsCredentialId",
                table: "Devices",
                column: "RouterOsCredentialId",
                principalTable: "RouterOsCredentials",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MonitoringSettings_RouterOsCredentials_DefaultRouterOsCrede~",
                table: "MonitoringSettings",
                column: "DefaultRouterOsCredentialId",
                principalTable: "RouterOsCredentials",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Customers_RouterOsCredentials_RouterOsCredentialId",
                table: "Customers");

            migrationBuilder.DropForeignKey(
                name: "FK_Devices_RouterOsCredentials_RouterOsCredentialId",
                table: "Devices");

            migrationBuilder.DropForeignKey(
                name: "FK_MonitoringSettings_RouterOsCredentials_DefaultRouterOsCrede~",
                table: "MonitoringSettings");

            migrationBuilder.DropTable(
                name: "RouterOsCredentials");

            migrationBuilder.DropIndex(
                name: "IX_MonitoringSettings_DefaultRouterOsCredentialId",
                table: "MonitoringSettings");

            migrationBuilder.DropIndex(
                name: "IX_Devices_RouterOsCredentialId",
                table: "Devices");

            migrationBuilder.DropIndex(
                name: "IX_Customers_RouterOsCredentialId",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "DefaultRouterOsCredentialId",
                table: "MonitoringSettings");

            migrationBuilder.DropColumn(
                name: "RouterOsCredentialId",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "RouterOsCredentialId",
                table: "Customers");
        }
    }
}
