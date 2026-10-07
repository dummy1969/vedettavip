using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace VedettaVip.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Devices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Address = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Icon = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SnmpVersion = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    SnmpCredentialId = table.Column<Guid>(type: "uuid", nullable: true),
                    RouterOsApiEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    ParentDeviceId = table.Column<Guid>(type: "uuid", nullable: true),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Devices", x => x.Id);
                    table.CheckConstraint("CK_Devices_ParentNotSelf", "\"ParentDeviceId\" IS NULL OR \"ParentDeviceId\" <> \"Id\"");
                    table.ForeignKey(
                        name: "FK_Devices_Devices_ParentDeviceId",
                        column: x => x.ParentDeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Maps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ParentMapId = table.Column<Guid>(type: "uuid", nullable: true),
                    BackgroundImage = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    GridSize = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Maps", x => x.Id);
                    table.CheckConstraint("CK_Maps_GridSize", "\"GridSize\" > 0");
                    table.CheckConstraint("CK_Maps_ParentNotSelf", "\"ParentMapId\" IS NULL OR \"ParentMapId\" <> \"Id\"");
                    table.ForeignKey(
                        name: "FK_Maps_Maps_ParentMapId",
                        column: x => x.ParentMapId,
                        principalTable: "Maps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Events",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Severity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Message = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    Acknowledged = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Events_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MapNodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MapId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: true),
                    SubmapId = table.Column<Guid>(type: "uuid", nullable: true),
                    X = table.Column<double>(type: "double precision", nullable: false),
                    Y = table.Column<double>(type: "double precision", nullable: false),
                    LabelTemplate = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Icon = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MapNodes", x => x.Id);
                    table.CheckConstraint("CK_MapNodes_Kind", "(\"Kind\" = 'Device' AND \"DeviceId\" IS NOT NULL AND \"SubmapId\" IS NULL) OR\n(\"Kind\" = 'Submap' AND \"SubmapId\" IS NOT NULL AND \"DeviceId\" IS NULL) OR\n(\"Kind\" = 'Static' AND \"DeviceId\" IS NULL AND \"SubmapId\" IS NULL)");
                    table.CheckConstraint("CK_MapNodes_SubmapNotSelf", "\"SubmapId\" IS NULL OR \"SubmapId\" <> \"MapId\"");
                    table.ForeignKey(
                        name: "FK_MapNodes_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MapNodes_Maps_MapId",
                        column: x => x.MapId,
                        principalTable: "Maps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MapNodes_Maps_SubmapId",
                        column: x => x.SubmapId,
                        principalTable: "Maps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MapLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MapId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromNodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    ToNodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: true),
                    IfIndex = table.Column<int>(type: "integer", nullable: true),
                    SpeedBps = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MapLinks", x => x.Id);
                    table.CheckConstraint("CK_MapLinks_IfIndexNeedsDevice", "\"IfIndex\" IS NULL OR \"DeviceId\" IS NOT NULL");
                    table.CheckConstraint("CK_MapLinks_NotLoop", "\"FromNodeId\" <> \"ToNodeId\"");
                    table.CheckConstraint("CK_MapLinks_SpeedBps", "\"SpeedBps\" > 0");
                    table.ForeignKey(
                        name: "FK_MapLinks_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MapLinks_MapNodes_FromNodeId",
                        column: x => x.FromNodeId,
                        principalTable: "MapNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MapLinks_MapNodes_ToNodeId",
                        column: x => x.ToNodeId,
                        principalTable: "MapNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MapLinks_Maps_MapId",
                        column: x => x.MapId,
                        principalTable: "Maps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Devices",
                columns: new[] { "Id", "Address", "Enabled", "Icon", "Name", "ParentDeviceId", "RouterOsApiEnabled", "SnmpCredentialId", "SnmpVersion", "Type" },
                values: new object[] { new Guid("0198f000-0000-7000-8000-000000000101"), "10.0.0.1", true, null, "CCR2004-CORE", null, true, null, "V2c", "Router" });

            migrationBuilder.InsertData(
                table: "Maps",
                columns: new[] { "Id", "BackgroundImage", "GridSize", "Name", "ParentMapId" },
                values: new object[] { new Guid("0198f000-0000-7000-8000-000000000001"), null, 20, "Sede principale", null });

            migrationBuilder.InsertData(
                table: "Devices",
                columns: new[] { "Id", "Address", "Enabled", "Icon", "Name", "ParentDeviceId", "RouterOsApiEnabled", "SnmpCredentialId", "SnmpVersion", "Type" },
                values: new object[,]
                {
                    { new Guid("0198f000-0000-7000-8000-000000000102"), "10.0.0.2", true, null, "CRS326-SW1", new Guid("0198f000-0000-7000-8000-000000000101"), true, null, "V2c", "Switch" },
                    { new Guid("0198f000-0000-7000-8000-000000000103"), "10.0.0.3", true, null, "CRS326-SW2", new Guid("0198f000-0000-7000-8000-000000000101"), true, null, "V2c", "Switch" }
                });

            migrationBuilder.InsertData(
                table: "MapNodes",
                columns: new[] { "Id", "DeviceId", "Icon", "Kind", "LabelTemplate", "MapId", "SubmapId", "X", "Y" },
                values: new object[,]
                {
                    { new Guid("0198f000-0000-7000-8000-000000000201"), new Guid("0198f000-0000-7000-8000-000000000101"), null, "Device", "[Name]\n[Address]\nCPU: [Cpu]%", new Guid("0198f000-0000-7000-8000-000000000001"), null, 400.0, 100.0 },
                    { new Guid("0198f000-0000-7000-8000-000000000206"), null, null, "Static", "Internet", new Guid("0198f000-0000-7000-8000-000000000001"), null, 700.0, 100.0 }
                });

            migrationBuilder.InsertData(
                table: "Maps",
                columns: new[] { "Id", "BackgroundImage", "GridSize", "Name", "ParentMapId" },
                values: new object[] { new Guid("0198f000-0000-7000-8000-000000000002"), null, 20, "Filiale Nord", new Guid("0198f000-0000-7000-8000-000000000001") });

            migrationBuilder.InsertData(
                table: "Devices",
                columns: new[] { "Id", "Address", "Enabled", "Icon", "Name", "ParentDeviceId", "RouterOsApiEnabled", "SnmpCredentialId", "SnmpVersion", "Type" },
                values: new object[] { new Guid("0198f000-0000-7000-8000-000000000104"), "10.0.10.5", true, null, "cAP-ax-Uffici", new Guid("0198f000-0000-7000-8000-000000000102"), true, null, "V2c", "AccessPoint" });

            migrationBuilder.InsertData(
                table: "MapLinks",
                columns: new[] { "Id", "DeviceId", "FromNodeId", "IfIndex", "MapId", "SpeedBps", "ToNodeId" },
                values: new object[] { new Guid("0198f000-0000-7000-8000-000000000305"), null, new Guid("0198f000-0000-7000-8000-000000000201"), null, new Guid("0198f000-0000-7000-8000-000000000001"), 1000000000L, new Guid("0198f000-0000-7000-8000-000000000206") });

            migrationBuilder.InsertData(
                table: "MapNodes",
                columns: new[] { "Id", "DeviceId", "Icon", "Kind", "LabelTemplate", "MapId", "SubmapId", "X", "Y" },
                values: new object[,]
                {
                    { new Guid("0198f000-0000-7000-8000-000000000202"), new Guid("0198f000-0000-7000-8000-000000000102"), null, "Device", "[Name]\n[Address]\nCPU: [Cpu]%", new Guid("0198f000-0000-7000-8000-000000000001"), null, 200.0, 300.0 },
                    { new Guid("0198f000-0000-7000-8000-000000000203"), new Guid("0198f000-0000-7000-8000-000000000103"), null, "Device", "[Name]\n[Address]\nCPU: [Cpu]%", new Guid("0198f000-0000-7000-8000-000000000001"), null, 600.0, 300.0 },
                    { new Guid("0198f000-0000-7000-8000-000000000205"), null, null, "Submap", "[Name]\n(sottomappa)", new Guid("0198f000-0000-7000-8000-000000000001"), new Guid("0198f000-0000-7000-8000-000000000002"), 640.0, 480.0 }
                });

            migrationBuilder.InsertData(
                table: "MapLinks",
                columns: new[] { "Id", "DeviceId", "FromNodeId", "IfIndex", "MapId", "SpeedBps", "ToNodeId" },
                values: new object[,]
                {
                    { new Guid("0198f000-0000-7000-8000-000000000301"), null, new Guid("0198f000-0000-7000-8000-000000000201"), null, new Guid("0198f000-0000-7000-8000-000000000001"), 10000000000L, new Guid("0198f000-0000-7000-8000-000000000202") },
                    { new Guid("0198f000-0000-7000-8000-000000000302"), null, new Guid("0198f000-0000-7000-8000-000000000201"), null, new Guid("0198f000-0000-7000-8000-000000000001"), 10000000000L, new Guid("0198f000-0000-7000-8000-000000000203") },
                    { new Guid("0198f000-0000-7000-8000-000000000304"), null, new Guid("0198f000-0000-7000-8000-000000000203"), null, new Guid("0198f000-0000-7000-8000-000000000001"), 100000000L, new Guid("0198f000-0000-7000-8000-000000000205") }
                });

            migrationBuilder.InsertData(
                table: "MapNodes",
                columns: new[] { "Id", "DeviceId", "Icon", "Kind", "LabelTemplate", "MapId", "SubmapId", "X", "Y" },
                values: new object[] { new Guid("0198f000-0000-7000-8000-000000000204"), new Guid("0198f000-0000-7000-8000-000000000104"), null, "Device", "[Name]\n[Address]\nCPU: [Cpu]%", new Guid("0198f000-0000-7000-8000-000000000001"), null, 200.0, 480.0 });

            migrationBuilder.InsertData(
                table: "MapLinks",
                columns: new[] { "Id", "DeviceId", "FromNodeId", "IfIndex", "MapId", "SpeedBps", "ToNodeId" },
                values: new object[] { new Guid("0198f000-0000-7000-8000-000000000303"), null, new Guid("0198f000-0000-7000-8000-000000000202"), null, new Guid("0198f000-0000-7000-8000-000000000001"), 1000000000L, new Guid("0198f000-0000-7000-8000-000000000204") });

            migrationBuilder.CreateIndex(
                name: "IX_Devices_Name",
                table: "Devices",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Devices_ParentDeviceId",
                table: "Devices",
                column: "ParentDeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_Events_DeviceId_Time",
                table: "Events",
                columns: new[] { "DeviceId", "Time" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_Events_Unacknowledged",
                table: "Events",
                column: "Time",
                filter: "NOT \"Acknowledged\"");

            migrationBuilder.CreateIndex(
                name: "IX_MapLinks_DeviceId",
                table: "MapLinks",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_MapLinks_FromNodeId",
                table: "MapLinks",
                column: "FromNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_MapLinks_MapId",
                table: "MapLinks",
                column: "MapId");

            migrationBuilder.CreateIndex(
                name: "IX_MapLinks_ToNodeId",
                table: "MapLinks",
                column: "ToNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_MapNodes_DeviceId",
                table: "MapNodes",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_MapNodes_MapId_DeviceId",
                table: "MapNodes",
                columns: new[] { "MapId", "DeviceId" },
                unique: true,
                filter: "\"DeviceId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MapNodes_SubmapId",
                table: "MapNodes",
                column: "SubmapId",
                unique: true,
                filter: "\"SubmapId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Maps_ParentMapId",
                table: "Maps",
                column: "ParentMapId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Events");

            migrationBuilder.DropTable(
                name: "MapLinks");

            migrationBuilder.DropTable(
                name: "MapNodes");

            migrationBuilder.DropTable(
                name: "Devices");

            migrationBuilder.DropTable(
                name: "Maps");
        }
    }
}
