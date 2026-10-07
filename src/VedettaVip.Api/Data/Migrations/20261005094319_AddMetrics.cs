using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VedettaVip.Api.Data.Migrations
{
    /// <summary>
    /// Metriche su TimescaleDB, fuori dal modello EF (scrittura con COPY binario, lettura con SQL e time_bucket):
    /// <list type="bullet">
    /// <item>"Metrics": hypertable dei campioni grezzi (30 s), chunk giornalieri, compressione dopo 2 giorni,
    /// retention 7 giorni. Nessuna FK verso Devices: l'ingest non deve fallire per un device appena eliminato.</item>
    /// <item>"Metrics5m": continuous aggregate a 5 minuti (avg/max/min/campioni), retention 90 giorni.</item>
    /// <item>"Metrics1h": continuous aggregate a 1 ora costruito su "Metrics5m", retention 2 anni.</item>
    /// </list>
    /// Gli aggregati sono real-time (materialized_only = false): includono anche i bucket non ancora materializzati.
    /// CREATE MATERIALIZED VIEW ... timescaledb.continuous non può stare in una transazione: tutte le istruzioni
    /// sono fuori transazione e idempotenti, così una migration interrotta a metà si può rilanciare.
    /// </summary>
    public partial class AddMetrics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS timescaledb;", suppressTransaction: true);

            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS "Metrics" (
                    "Time"     timestamp with time zone NOT NULL,
                    "DeviceId" uuid                     NOT NULL,
                    "IfIndex"  integer                  NULL,
                    "Name"     character varying(64)    NOT NULL,
                    "Value"    double precision         NOT NULL
                );
                """, suppressTransaction: true);

            migrationBuilder.Sql("""
                SELECT create_hypertable('"Metrics"', by_range('Time', INTERVAL '1 day'), if_not_exists => TRUE);
                """, suppressTransaction: true);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_Metrics_Series" ON "Metrics" ("DeviceId", "Name", "IfIndex", "Time" DESC);
                """, suppressTransaction: true);

            migrationBuilder.Sql("""
                ALTER TABLE "Metrics" SET (
                    timescaledb.compress,
                    timescaledb.compress_segmentby = '"DeviceId", "Name", "IfIndex"',
                    timescaledb.compress_orderby = '"Time" DESC');
                """, suppressTransaction: true);

            migrationBuilder.Sql("""
                SELECT add_compression_policy('"Metrics"', INTERVAL '2 days', if_not_exists => TRUE);
                SELECT add_retention_policy('"Metrics"', INTERVAL '7 days', if_not_exists => TRUE);
                """, suppressTransaction: true);

            migrationBuilder.Sql("""
                CREATE MATERIALIZED VIEW IF NOT EXISTS "Metrics5m"
                WITH (timescaledb.continuous, timescaledb.materialized_only = false) AS
                SELECT time_bucket(INTERVAL '5 minutes', "Time") AS "Bucket",
                       "DeviceId", "IfIndex", "Name",
                       avg("Value") AS "Avg", max("Value") AS "Max", min("Value") AS "Min", count(*) AS "Samples"
                FROM "Metrics"
                GROUP BY 1, 2, 3, 4
                WITH NO DATA;
                """, suppressTransaction: true);

            // La finestra di refresh (1 giorno) deve stare dentro la retention dei grezzi (7 giorni)
            migrationBuilder.Sql("""
                SELECT add_continuous_aggregate_policy('"Metrics5m"',
                    start_offset => INTERVAL '1 day', end_offset => INTERVAL '5 minutes',
                    schedule_interval => INTERVAL '5 minutes', if_not_exists => TRUE);
                SELECT add_retention_policy('"Metrics5m"', INTERVAL '90 days', if_not_exists => TRUE);
                """, suppressTransaction: true);

            // Media pesata sui campioni: la media delle medie a 5 minuti sarebbe sbagliata con bucket incompleti
            migrationBuilder.Sql("""
                CREATE MATERIALIZED VIEW IF NOT EXISTS "Metrics1h"
                WITH (timescaledb.continuous, timescaledb.materialized_only = false) AS
                SELECT time_bucket(INTERVAL '1 hour', "Bucket") AS "Bucket",
                       "DeviceId", "IfIndex", "Name",
                       sum("Avg" * "Samples") / sum("Samples") AS "Avg",
                       max("Max") AS "Max", min("Min") AS "Min", sum("Samples") AS "Samples"
                FROM "Metrics5m"
                GROUP BY 1, 2, 3, 4
                WITH NO DATA;
                """, suppressTransaction: true);

            migrationBuilder.Sql("""
                SELECT add_continuous_aggregate_policy('"Metrics1h"',
                    start_offset => INTERVAL '3 days', end_offset => INTERVAL '1 hour',
                    schedule_interval => INTERVAL '1 hour', if_not_exists => TRUE);
                SELECT add_retention_policy('"Metrics1h"', INTERVAL '730 days', if_not_exists => TRUE);
                """, suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP MATERIALIZED VIEW IF EXISTS "Metrics1h";""", suppressTransaction: true);
            migrationBuilder.Sql("""DROP MATERIALIZED VIEW IF EXISTS "Metrics5m";""", suppressTransaction: true);
            migrationBuilder.Sql("""DROP TABLE IF EXISTS "Metrics";""", suppressTransaction: true);
        }
    }
}
