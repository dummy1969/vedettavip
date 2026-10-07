using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VedettaVip.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDiscovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DeviceNeighbors",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Protocol = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    LocalInterface = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    LocalIfIndex = table.Column<int>(type: "integer", nullable: true),
                    Identity = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    MacAddress = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    RemoteInterface = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Platform = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Board = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Capabilities = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    SeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceNeighbors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeviceNeighbors_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DiscoveryIgnores",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Label = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiscoveryIgnores", x => x.Key);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeviceNeighbors_DeviceId",
                table: "DeviceNeighbors",
                column: "DeviceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeviceNeighbors");

            migrationBuilder.DropTable(
                name: "DiscoveryIgnores");
        }
    }
}
