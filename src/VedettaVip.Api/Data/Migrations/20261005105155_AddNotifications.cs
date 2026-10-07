using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace VedettaVip.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "DeviceId",
                table: "Events",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "AgentId",
                table: "Events",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FromState",
                table: "Events",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NotifyAfter",
                table: "Events",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NotifyNote",
                table: "Events",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NotifyState",
                table: "Events",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "None"); // eventi già esistenti: storici, da non notificare

            migrationBuilder.AddColumn<string>(
                name: "ToState",
                table: "Events",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CustomerId",
                table: "Devices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OfflineSince",
                table: "Agents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Contacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    TelegramChatId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Contacts", x => x.Id);
                    table.CheckConstraint("CK_Contacts_Channel", "\"Email\" IS NOT NULL OR \"TelegramChatId\" IS NOT NULL");
                });

            migrationBuilder.CreateTable(
                name: "Customers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Notes = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Customers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DataProtectionKeys",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FriendlyName = table.Column<string>(type: "text", nullable: true),
                    Xml = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataProtectionKeys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NotificationSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    EmailEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    SmtpHost = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    SmtpPort = table.Column<int>(type: "integer", nullable: false),
                    SmtpSecurity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SmtpUsername = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    SmtpPasswordProtected = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    SmtpFromAddress = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    SmtpFromName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    SmtpValidateCertificate = table.Column<bool>(type: "boolean", nullable: false),
                    TelegramEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    TelegramBotTokenProtected = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    DelaySeconds = table.Column<int>(type: "integer", nullable: false),
                    TimeZoneId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PublicUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationSettings", x => x.Id);
                    table.CheckConstraint("CK_NotificationSettings_Delay", "\"DelaySeconds\" BETWEEN 0 AND 3600");
                    table.CheckConstraint("CK_NotificationSettings_Port", "\"SmtpPort\" BETWEEN 1 AND 65535");
                    table.CheckConstraint("CK_NotificationSettings_Singleton", "\"Id\" = 1");
                });

            migrationBuilder.CreateTable(
                name: "NotificationDeliveries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EventId = table.Column<long>(type: "bigint", nullable: false),
                    ContactId = table.Column<Guid>(type: "uuid", nullable: true),
                    ContactName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Channel = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Destination = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastError = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationDeliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotificationDeliveries_Contacts_ContactId",
                        column: x => x.ContactId,
                        principalTable: "Contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_NotificationDeliveries_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Subscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ContactId = table.Column<Guid>(type: "uuid", nullable: false),
                    Scope = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    MapId = table.Column<Guid>(type: "uuid", nullable: true),
                    Filter = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    NotifyRecovery = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Subscriptions", x => x.Id);
                    table.CheckConstraint("CK_Subscriptions_Scope", "(\"Scope\" = 'All' AND \"CustomerId\" IS NULL AND \"MapId\" IS NULL) OR\n(\"Scope\" = 'Customer' AND \"CustomerId\" IS NOT NULL AND \"MapId\" IS NULL) OR\n(\"Scope\" = 'Map' AND \"MapId\" IS NOT NULL AND \"CustomerId\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_Subscriptions_Contacts_ContactId",
                        column: x => x.ContactId,
                        principalTable: "Contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Subscriptions_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Subscriptions_Maps_MapId",
                        column: x => x.MapId,
                        principalTable: "Maps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "Devices",
                keyColumn: "Id",
                keyValue: new Guid("0198f000-0000-7000-8000-000000000101"),
                column: "CustomerId",
                value: null);

            migrationBuilder.UpdateData(
                table: "Devices",
                keyColumn: "Id",
                keyValue: new Guid("0198f000-0000-7000-8000-000000000102"),
                column: "CustomerId",
                value: null);

            migrationBuilder.UpdateData(
                table: "Devices",
                keyColumn: "Id",
                keyValue: new Guid("0198f000-0000-7000-8000-000000000103"),
                column: "CustomerId",
                value: null);

            migrationBuilder.UpdateData(
                table: "Devices",
                keyColumn: "Id",
                keyValue: new Guid("0198f000-0000-7000-8000-000000000104"),
                column: "CustomerId",
                value: null);

            migrationBuilder.InsertData(
                table: "NotificationSettings",
                columns: new[] { "Id", "DelaySeconds", "EmailEnabled", "PublicUrl", "SmtpFromAddress", "SmtpFromName", "SmtpHost", "SmtpPasswordProtected", "SmtpPort", "SmtpSecurity", "SmtpUsername", "SmtpValidateCertificate", "TelegramBotTokenProtected", "TelegramEnabled", "TimeZoneId" },
                values: new object[] { 1, 60, false, null, null, null, null, null, 587, "StartTls", null, true, null, false, "Europe/Rome" });

            migrationBuilder.CreateIndex(
                name: "IX_Events_NotifyPending",
                table: "Events",
                column: "NotifyAfter",
                filter: "\"NotifyState\" = 'Pending'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Events_Subject",
                table: "Events",
                sql: "\"DeviceId\" IS NOT NULL OR \"AgentId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Devices_CustomerId",
                table: "Devices",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_Name",
                table: "Customers",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveries_ContactId",
                table: "NotificationDeliveries",
                column: "ContactId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveries_EventId",
                table: "NotificationDeliveries",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveries_Pending",
                table: "NotificationDeliveries",
                column: "NextAttemptAt",
                filter: "\"Status\" = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_ContactId",
                table: "Subscriptions",
                column: "ContactId");

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_CustomerId",
                table: "Subscriptions",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_MapId",
                table: "Subscriptions",
                column: "MapId");

            migrationBuilder.AddForeignKey(
                name: "FK_Devices_Customers_CustomerId",
                table: "Devices",
                column: "CustomerId",
                principalTable: "Customers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Devices_Customers_CustomerId",
                table: "Devices");

            migrationBuilder.DropTable(
                name: "DataProtectionKeys");

            migrationBuilder.DropTable(
                name: "NotificationDeliveries");

            migrationBuilder.DropTable(
                name: "NotificationSettings");

            migrationBuilder.DropTable(
                name: "Subscriptions");

            migrationBuilder.DropTable(
                name: "Contacts");

            migrationBuilder.DropTable(
                name: "Customers");

            migrationBuilder.DropIndex(
                name: "IX_Events_NotifyPending",
                table: "Events");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Events_Subject",
                table: "Events");

            migrationBuilder.DropIndex(
                name: "IX_Devices_CustomerId",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "AgentId",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "FromState",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "NotifyAfter",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "NotifyNote",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "NotifyState",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "ToState",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "CustomerId",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "OfflineSince",
                table: "Agents");

            migrationBuilder.AlterColumn<Guid>(
                name: "DeviceId",
                table: "Events",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
