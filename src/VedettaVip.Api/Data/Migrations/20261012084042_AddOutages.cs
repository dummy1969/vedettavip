using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VedettaVip.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOutages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Outages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Service = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Detail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EndNote = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Maintenance = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Outages", x => x.Id);
                    table.CheckConstraint("CK_Outages_Interval", "\"EndedAt\" IS NULL OR \"EndedAt\" >= \"StartedAt\"");
                    table.ForeignKey(
                        name: "FK_Outages_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Outages_DeviceId_StartedAt",
                table: "Outages",
                columns: new[] { "DeviceId", "StartedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_Outages_Open",
                table: "Outages",
                columns: new[] { "DeviceId", "Key" },
                unique: true,
                filter: "\"EndedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Outages_StartedAt",
                table: "Outages",
                column: "StartedAt",
                descending: new bool[0]);

            // Storico ricostruito dagli eventi già registrati (le interruzioni in corso si riallineano comunque al
            // primo report). Manutenzione: dalla nota del dispatcher ("in manutenzione: <finestra>").
            const string maintenance = """
                CASE WHEN s."NotifyNote" LIKE 'in manutenzione: %' THEN left(substr(s."NotifyNote", 18), 128)
                     WHEN s."NotifyState" = 'Maintenance' OR s."NotifyNote" LIKE '%manutenzione%' THEN 'manutenzione' END
                """;

            // Ping (Down) e SNMP (Partial): finiscono al cambio di stato successivo dello stesso device
            migrationBuilder.Sql($"""
                INSERT INTO "Outages" ("Id", "DeviceId", "Service", "Key", "Detail", "StartedAt", "EndedAt", "EndNote", "Maintenance")
                SELECT gen_random_uuid(), s."DeviceId",
                       CASE s."ToState" WHEN 'Down' THEN 'Ping' ELSE 'Snmp' END,
                       CASE s."ToState" WHEN 'Down' THEN 'ping' ELSE 'snmp' END,
                       CASE s."ToState" WHEN 'Down' THEN 'non risponde al ping' ELSE 'risponde al ping ma non a SNMP' END,
                       s."Time",
                       CASE WHEN s."NextTime" IS NOT NULL THEN s."NextTime"
                            WHEN d."Enabled" AND st."State" = s."ToState" THEN NULL
                            ELSE GREATEST(s."Time", COALESCE(s."ResolvedAt", st."LastReportAt", s."Time")) END,
                       CASE WHEN s."NextTime" IS NOT NULL OR (d."Enabled" AND st."State" = s."ToState") THEN NULL
                            WHEN NOT d."Enabled" THEN 'monitoraggio disattivato'
                            ELSE 'fine non registrata' END,
                       {maintenance}
                FROM (SELECT e.*, lead(e."Time") OVER (PARTITION BY e."DeviceId" ORDER BY e."Time", e."Id") AS "NextTime"
                      FROM "Events" e
                      WHERE e."Type" = 'StateChange' AND e."DeviceId" IS NOT NULL) s
                JOIN "Devices" d ON d."Id" = s."DeviceId"
                LEFT JOIN "DeviceStatuses" st ON st."DeviceId" = s."DeviceId"
                WHERE s."ToState" IN ('Down', 'Partial');
                """);

            // Soglie, interfacce e tunnel sorvegliati: dall'apertura (ThresholdRaised) all'evento successivo con la stessa
            // AlertKey (di norma la chiusura); senza, aperte solo se l'avviso è ancora aperto
            migrationBuilder.Sql($"""
                INSERT INTO "Outages" ("Id", "DeviceId", "Service", "Key", "Detail", "StartedAt", "EndedAt", "EndNote", "Maintenance")
                SELECT gen_random_uuid(), s."DeviceId",
                       CASE WHEN s."AlertKey" = 'latency' THEN 'Latency'
                            WHEN s."AlertKey" = 'loss' THEN 'Loss'
                            WHEN s."AlertKey" = 'ros.cpu' THEN 'Cpu'
                            WHEN s."AlertKey" = 'ros.temp' THEN 'Temperature'
                            WHEN s."AlertKey" LIKE 'link:%' THEN 'Link'
                            WHEN s."AlertKey" LIKE 'if:%' THEN 'Interface'
                            ELSE 'WireGuard' END,
                       s."AlertKey",
                       left(CASE WHEN strpos(s."Message", ': ') > 0 THEN substr(s."Message", strpos(s."Message", ': ') + 2)
                                 ELSE s."Message" END, 256),
                       s."Time",
                       CASE WHEN s."NextTime" IS NOT NULL THEN s."NextTime"
                            WHEN d."Enabled" AND s."StillOpen" THEN NULL
                            ELSE GREATEST(s."Time", COALESCE(s."ResolvedAt", s."AcknowledgedAt", s."Time")) END,
                       CASE WHEN s."NextTime" IS NOT NULL OR (d."Enabled" AND s."StillOpen") OR s."ResolvedAt" IS NOT NULL THEN NULL
                            WHEN NOT d."Enabled" THEN 'monitoraggio disattivato'
                            ELSE 'fine non registrata' END,
                       {maintenance}
                FROM (SELECT e.*,
                             lead(e."Time") OVER (PARTITION BY e."DeviceId", e."AlertKey" ORDER BY e."Time", e."Id") AS "NextTime",
                             EXISTS (SELECT 1 FROM "OpenThresholdAlerts" a
                                     WHERE a."DeviceId" = e."DeviceId"
                                       AND ((e."AlertKey" = 'latency' AND a."Kind" = 'Latency')
                                         OR (e."AlertKey" = 'loss' AND a."Kind" = 'Loss')
                                         OR (e."AlertKey" = 'ros.cpu' AND a."Kind" = 'RouterOsCpu')
                                         OR (e."AlertKey" = 'ros.temp' AND a."Kind" = 'RouterOsTemperature')
                                         OR (e."AlertKey" = 'link:' || a."LinkId"::text AND a."Kind" = 'LinkUtilization')))
                             OR EXISTS (SELECT 1 FROM "RouterOsWatches" w
                                        WHERE w."DeviceId" = e."DeviceId" AND w."State" = 2
                                          AND e."AlertKey" = CASE w."Kind" WHEN 0 THEN 'if:' ELSE 'wg:' END || w."Key") AS "StillOpen"
                      FROM "Events" e
                      WHERE e."Type" IN ('ThresholdRaised', 'ThresholdCleared') AND e."DeviceId" IS NOT NULL AND e."AlertKey" IS NOT NULL) s
                JOIN "Devices" d ON d."Id" = s."DeviceId"
                WHERE s."Type" = 'ThresholdRaised'
                  AND (s."AlertKey" IN ('latency', 'loss', 'ros.cpu', 'ros.temp')
                       OR s."AlertKey" LIKE 'link:%' OR s."AlertKey" LIKE 'if:%' OR s."AlertKey" LIKE 'wg:%');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Outages");
        }
    }
}
