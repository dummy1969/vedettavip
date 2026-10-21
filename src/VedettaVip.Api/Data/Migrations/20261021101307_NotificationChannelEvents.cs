using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VedettaVip.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class NotificationChannelEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Events_Subject",
                table: "Events");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Events_Subject",
                table: "Events",
                sql: "\"DeviceId\" IS NOT NULL OR \"AgentId\" IS NOT NULL OR \"Type\" IN ('NotificationsFailing', 'NotificationsRestored')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Events_Subject",
                table: "Events");

            // Gli eventi dei canali di notifica non rispettano il vincolo precedente (consegne in CASCADE)
            migrationBuilder.Sql("DELETE FROM \"Events\" WHERE \"DeviceId\" IS NULL AND \"AgentId\" IS NULL;");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Events_Subject",
                table: "Events",
                sql: "\"DeviceId\" IS NOT NULL OR \"AgentId\" IS NOT NULL");
        }
    }
}
