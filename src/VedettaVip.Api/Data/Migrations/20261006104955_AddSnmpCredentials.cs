using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VedettaVip.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSnmpCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DefaultSnmpCredentialId",
                table: "MonitoringSettings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SnmpCredentialId",
                table: "Customers",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SnmpCredentials",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CommunityProtected = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SnmpCredentials", x => x.Id);
                });

            migrationBuilder.UpdateData(
                table: "MonitoringSettings",
                keyColumn: "Id",
                keyValue: 1,
                column: "DefaultSnmpCredentialId",
                value: null);

            migrationBuilder.CreateIndex(
                name: "IX_MonitoringSettings_DefaultSnmpCredentialId",
                table: "MonitoringSettings",
                column: "DefaultSnmpCredentialId");

            // Prima di questa migration SnmpCredentialId non aveva una tabella di riferimento: valori orfani azzerati
            migrationBuilder.Sql("UPDATE \"Devices\" SET \"SnmpCredentialId\" = NULL WHERE \"SnmpCredentialId\" IS NOT NULL;");

            migrationBuilder.CreateIndex(
                name: "IX_Devices_SnmpCredentialId",
                table: "Devices",
                column: "SnmpCredentialId");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_SnmpCredentialId",
                table: "Customers",
                column: "SnmpCredentialId");

            migrationBuilder.CreateIndex(
                name: "IX_SnmpCredentials_Name",
                table: "SnmpCredentials",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Customers_SnmpCredentials_SnmpCredentialId",
                table: "Customers",
                column: "SnmpCredentialId",
                principalTable: "SnmpCredentials",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Devices_SnmpCredentials_SnmpCredentialId",
                table: "Devices",
                column: "SnmpCredentialId",
                principalTable: "SnmpCredentials",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MonitoringSettings_SnmpCredentials_DefaultSnmpCredentialId",
                table: "MonitoringSettings",
                column: "DefaultSnmpCredentialId",
                principalTable: "SnmpCredentials",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Customers_SnmpCredentials_SnmpCredentialId",
                table: "Customers");

            migrationBuilder.DropForeignKey(
                name: "FK_Devices_SnmpCredentials_SnmpCredentialId",
                table: "Devices");

            migrationBuilder.DropForeignKey(
                name: "FK_MonitoringSettings_SnmpCredentials_DefaultSnmpCredentialId",
                table: "MonitoringSettings");

            migrationBuilder.DropTable(
                name: "SnmpCredentials");

            migrationBuilder.DropIndex(
                name: "IX_MonitoringSettings_DefaultSnmpCredentialId",
                table: "MonitoringSettings");

            migrationBuilder.DropIndex(
                name: "IX_Devices_SnmpCredentialId",
                table: "Devices");

            migrationBuilder.DropIndex(
                name: "IX_Customers_SnmpCredentialId",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "DefaultSnmpCredentialId",
                table: "MonitoringSettings");

            migrationBuilder.DropColumn(
                name: "SnmpCredentialId",
                table: "Customers");
        }
    }
}
