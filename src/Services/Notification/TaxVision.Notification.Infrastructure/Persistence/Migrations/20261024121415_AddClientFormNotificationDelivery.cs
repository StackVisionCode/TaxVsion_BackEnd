using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Notification.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddClientFormNotificationDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "NotificationLogs",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: ""
            );

            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "CustomerContactPolicyProjections",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "En"
            );

            migrationBuilder.CreateTable(
                name: "ClientFormNotificationSettingsProjections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PushNotificationsEnabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientFormNotificationSettingsProjections", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_NotificationLogs_TenantId_RelatedEventId_Kind_Channel_Recipient",
                table: "NotificationLogs",
                columns: new[] { "TenantId", "RelatedEventId", "Kind", "Channel", "Recipient" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_ClientFormNotificationSettingsProjections_TenantId",
                table: "ClientFormNotificationSettingsProjections",
                column: "TenantId",
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ClientFormNotificationSettingsProjections");

            migrationBuilder.DropIndex(
                name: "IX_NotificationLogs_TenantId_RelatedEventId_Kind_Channel_Recipient",
                table: "NotificationLogs"
            );

            migrationBuilder.DropColumn(name: "Kind", table: "NotificationLogs");

            migrationBuilder.DropColumn(name: "Language", table: "CustomerContactPolicyProjections");
        }
    }
}
