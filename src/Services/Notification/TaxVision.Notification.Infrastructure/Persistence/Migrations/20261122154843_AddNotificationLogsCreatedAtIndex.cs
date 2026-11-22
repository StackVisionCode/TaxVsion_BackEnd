using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Notification.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationLogsCreatedAtIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_NotificationLogs_CreatedAtUtc",
                table: "NotificationLogs",
                column: "CreatedAtUtc"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_NotificationLogs_CreatedAtUtc", table: "NotificationLogs");
        }
    }
}
