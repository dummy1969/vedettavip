using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VedettaVip.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoteAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "HttpDefaultPort",
                table: "MonitoringSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HttpsDefaultPort",
                table: "MonitoringSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PuttyLinuxPath",
                table: "MonitoringSettings",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PuttyWindowsPath",
                table: "MonitoringSettings",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SshDefaultPort",
                table: "MonitoringSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TelnetDefaultPort",
                table: "MonitoringSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TerminalAccess",
                table: "Devices",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "None"); // a mano: EF metterebbe "", che non è un valore dell'enum

            migrationBuilder.AddColumn<int>(
                name: "TerminalPort",
                table: "Devices",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WebAccess",
                table: "Devices",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "None"); // a mano: come sopra

            migrationBuilder.AddColumn<int>(
                name: "WebPort",
                table: "Devices",
                type: "integer",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Devices",
                keyColumn: "Id",
                keyValue: new Guid("0198f000-0000-7000-8000-000000000101"),
                columns: new[] { "TerminalAccess", "TerminalPort", "WebAccess", "WebPort" },
                values: new object[] { "None", null, "None", null });

            migrationBuilder.UpdateData(
                table: "Devices",
                keyColumn: "Id",
                keyValue: new Guid("0198f000-0000-7000-8000-000000000102"),
                columns: new[] { "TerminalAccess", "TerminalPort", "WebAccess", "WebPort" },
                values: new object[] { "None", null, "None", null });

            migrationBuilder.UpdateData(
                table: "Devices",
                keyColumn: "Id",
                keyValue: new Guid("0198f000-0000-7000-8000-000000000103"),
                columns: new[] { "TerminalAccess", "TerminalPort", "WebAccess", "WebPort" },
                values: new object[] { "None", null, "None", null });

            migrationBuilder.UpdateData(
                table: "Devices",
                keyColumn: "Id",
                keyValue: new Guid("0198f000-0000-7000-8000-000000000104"),
                columns: new[] { "TerminalAccess", "TerminalPort", "WebAccess", "WebPort" },
                values: new object[] { "None", null, "None", null });

            migrationBuilder.UpdateData(
                table: "MonitoringSettings",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "HttpDefaultPort", "HttpsDefaultPort", "PuttyLinuxPath", "PuttyWindowsPath", "SshDefaultPort", "TelnetDefaultPort" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.AddCheckConstraint(
                name: "CK_MonitoringSettings_AccessPorts",
                table: "MonitoringSettings",
                sql: "(\"HttpDefaultPort\" IS NULL OR \"HttpDefaultPort\" BETWEEN 1 AND 65535) AND (\"HttpsDefaultPort\" IS NULL OR \"HttpsDefaultPort\" BETWEEN 1 AND 65535) AND (\"SshDefaultPort\" IS NULL OR \"SshDefaultPort\" BETWEEN 1 AND 65535) AND (\"TelnetDefaultPort\" IS NULL OR \"TelnetDefaultPort\" BETWEEN 1 AND 65535)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Devices_TerminalPort",
                table: "Devices",
                sql: "\"TerminalPort\" IS NULL OR \"TerminalPort\" BETWEEN 1 AND 65535");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Devices_WebPort",
                table: "Devices",
                sql: "\"WebPort\" IS NULL OR \"WebPort\" BETWEEN 1 AND 65535");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_MonitoringSettings_AccessPorts",
                table: "MonitoringSettings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Devices_TerminalPort",
                table: "Devices");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Devices_WebPort",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "HttpDefaultPort",
                table: "MonitoringSettings");

            migrationBuilder.DropColumn(
                name: "HttpsDefaultPort",
                table: "MonitoringSettings");

            migrationBuilder.DropColumn(
                name: "PuttyLinuxPath",
                table: "MonitoringSettings");

            migrationBuilder.DropColumn(
                name: "PuttyWindowsPath",
                table: "MonitoringSettings");

            migrationBuilder.DropColumn(
                name: "SshDefaultPort",
                table: "MonitoringSettings");

            migrationBuilder.DropColumn(
                name: "TelnetDefaultPort",
                table: "MonitoringSettings");

            migrationBuilder.DropColumn(
                name: "TerminalAccess",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "TerminalPort",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "WebAccess",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "WebPort",
                table: "Devices");
        }
    }
}
