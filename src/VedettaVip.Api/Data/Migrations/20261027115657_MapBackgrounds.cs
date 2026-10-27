using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VedettaVip.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class MapBackgrounds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MapTileAttribution",
                table: "MonitoringSettings",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MapTileAttributionUrl",
                table: "MonitoringSettings",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MapTileUrl",
                table: "MonitoringSettings",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BackgroundMode",
                table: "Maps",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "None"); // mappe esistenti: nessuno sfondo, come prima

            migrationBuilder.AddColumn<int>(
                name: "BackgroundOpacityPct",
                table: "Maps",
                type: "integer",
                nullable: false,
                defaultValue: 100); // i CHECK sotto vogliono 10–100

            migrationBuilder.AddColumn<int>(
                name: "BackgroundScalePct",
                table: "Maps",
                type: "integer",
                nullable: false,
                defaultValue: 100); // i CHECK sotto vogliono 1–1000

            migrationBuilder.AddColumn<bool>(
                name: "BackgroundShowGrid",
                table: "Maps",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<double>(
                name: "GeoLatitude",
                table: "Maps",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "GeoLongitude",
                table: "Maps",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GeoZoom",
                table: "Maps",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MapBackgrounds",
                columns: table => new
                {
                    MapId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Data = table.Column<byte[]>(type: "bytea", nullable: false),
                    Width = table.Column<int>(type: "integer", nullable: false),
                    Height = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MapBackgrounds", x => x.MapId);
                    table.CheckConstraint("CK_MapBackgrounds_Size", "\"Width\" > 0 AND \"Height\" > 0");
                    table.ForeignKey(
                        name: "FK_MapBackgrounds_Maps_MapId",
                        column: x => x.MapId,
                        principalTable: "Maps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "Maps",
                keyColumn: "Id",
                keyValue: new Guid("0198f000-0000-7000-8000-000000000001"),
                columns: new[] { "BackgroundMode", "BackgroundOpacityPct", "BackgroundScalePct", "BackgroundShowGrid", "GeoLatitude", "GeoLongitude", "GeoZoom" },
                values: new object[] { "None", 100, 100, false, null, null, null });

            migrationBuilder.UpdateData(
                table: "Maps",
                keyColumn: "Id",
                keyValue: new Guid("0198f000-0000-7000-8000-000000000002"),
                columns: new[] { "BackgroundMode", "BackgroundOpacityPct", "BackgroundScalePct", "BackgroundShowGrid", "GeoLatitude", "GeoLongitude", "GeoZoom" },
                values: new object[] { "None", 100, 100, false, null, null, null });

            migrationBuilder.UpdateData(
                table: "MonitoringSettings",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "MapTileAttribution", "MapTileAttributionUrl", "MapTileUrl" },
                values: new object[] { null, null, null });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Maps_BackgroundOpacity",
                table: "Maps",
                sql: "\"BackgroundOpacityPct\" BETWEEN 10 AND 100");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Maps_BackgroundScale",
                table: "Maps",
                sql: "\"BackgroundScalePct\" BETWEEN 1 AND 1000");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Maps_Geo",
                table: "Maps",
                sql: "(\"GeoLatitude\" IS NULL AND \"GeoLongitude\" IS NULL AND \"GeoZoom\" IS NULL) OR (\"GeoLatitude\" BETWEEN -85.0511 AND 85.0511 AND \"GeoLongitude\" BETWEEN -180 AND 180 AND \"GeoZoom\" BETWEEN 1 AND 19)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Maps_GeoMode",
                table: "Maps",
                sql: "\"BackgroundMode\" <> 'Geo' OR \"GeoZoom\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MapBackgrounds");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Maps_BackgroundOpacity",
                table: "Maps");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Maps_BackgroundScale",
                table: "Maps");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Maps_Geo",
                table: "Maps");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Maps_GeoMode",
                table: "Maps");

            migrationBuilder.DropColumn(
                name: "MapTileAttribution",
                table: "MonitoringSettings");

            migrationBuilder.DropColumn(
                name: "MapTileAttributionUrl",
                table: "MonitoringSettings");

            migrationBuilder.DropColumn(
                name: "MapTileUrl",
                table: "MonitoringSettings");

            migrationBuilder.DropColumn(
                name: "BackgroundMode",
                table: "Maps");

            migrationBuilder.DropColumn(
                name: "BackgroundOpacityPct",
                table: "Maps");

            migrationBuilder.DropColumn(
                name: "BackgroundScalePct",
                table: "Maps");

            migrationBuilder.DropColumn(
                name: "BackgroundShowGrid",
                table: "Maps");

            migrationBuilder.DropColumn(
                name: "GeoLatitude",
                table: "Maps");

            migrationBuilder.DropColumn(
                name: "GeoLongitude",
                table: "Maps");

            migrationBuilder.DropColumn(
                name: "GeoZoom",
                table: "Maps");
        }
    }
}
