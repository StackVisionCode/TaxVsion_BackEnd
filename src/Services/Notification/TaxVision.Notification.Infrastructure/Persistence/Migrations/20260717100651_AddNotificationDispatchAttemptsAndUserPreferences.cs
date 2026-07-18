using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Notification.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationDispatchAttemptsAndUserPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NotificationDispatchAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NotificationLogId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Channel = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: false),
                    ProviderMessageId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    QueuedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastEventAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ErrorReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Metadata = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationDispatchAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotificationDispatchAttempts_NotificationLogs_NotificationLogId",
                        column: x => x.NotificationLogId,
                        principalTable: "NotificationLogs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "UserNotificationPreferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Channel = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    Digest = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserNotificationPreferences", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDispatchAttempts_NotificationLogId",
                table: "NotificationDispatchAttempts",
                column: "NotificationLogId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDispatchAttempts_TenantId_Channel_Status_QueuedAtUtc",
                table: "NotificationDispatchAttempts",
                columns: new[] { "TenantId", "Channel", "Status", "QueuedAtUtc" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_UserNotificationPreferences_TenantId_UserId_Kind_Channel",
                table: "UserNotificationPreferences",
                columns: new[] { "TenantId", "UserId", "Kind", "Channel" },
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "NotificationDispatchAttempts");

            migrationBuilder.DropTable(name: "UserNotificationPreferences");
        }
    }
}
