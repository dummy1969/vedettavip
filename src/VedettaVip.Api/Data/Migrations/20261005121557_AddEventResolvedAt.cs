using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VedettaVip.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEventResolvedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ResolvedAt",
                table: "Events",
                type: "timestamp with time zone",
                nullable: true);

            // Gli eventi informativi (ripristini, prime rilevazioni) non richiedono gestione: come quelli nuovi
            migrationBuilder.Sql("""UPDATE "Events" SET "Acknowledged" = TRUE WHERE "Severity" = 'Info' AND NOT "Acknowledged";""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ResolvedAt",
                table: "Events");
        }
    }
}
