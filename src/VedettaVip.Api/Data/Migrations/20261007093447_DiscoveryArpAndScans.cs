using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VedettaVip.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class DiscoveryArpAndScans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DeviceArpEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MacAddress = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Interface = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    IfIndex = table.Column<int>(type: "integer", nullable: true),
                    HostName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Comment = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    SeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceArpEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeviceArpEntries_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DiscoveryScans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Cidr = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    MapId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    AgentId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Scanned = table.Column<int>(type: "integer", nullable: false),
                    Total = table.Column<int>(type: "integer", nullable: false),
                    Error = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiscoveryScans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DiscoveryScanHosts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ScanId = table.Column<Guid>(type: "uuid", nullable: false),
                    Address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RttMs = table.Column<double>(type: "double precision", nullable: true),
                    DnsName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    SysName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    SysDescr = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    SnmpProfileId = table.Column<Guid>(type: "uuid", nullable: true),
                    SnmpFallback = table.Column<bool>(type: "boolean", nullable: false),
                    OpenPorts = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiscoveryScanHosts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DiscoveryScanHosts_DiscoveryScans_ScanId",
                        column: x => x.ScanId,
                        principalTable: "DiscoveryScans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeviceArpEntries_DeviceId",
                table: "DeviceArpEntries",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_DiscoveryScanHosts_ScanId",
                table: "DiscoveryScanHosts",
                column: "ScanId");

            migrationBuilder.CreateIndex(
                name: "IX_DiscoveryScans_CreatedAt",
                table: "DiscoveryScans",
                column: "CreatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeviceArpEntries");

            migrationBuilder.DropTable(
                name: "DiscoveryScanHosts");

            migrationBuilder.DropTable(
                name: "DiscoveryScans");
        }
    }
}
