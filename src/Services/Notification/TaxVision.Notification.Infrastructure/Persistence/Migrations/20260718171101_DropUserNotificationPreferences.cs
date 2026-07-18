using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Notification.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DropUserNotificationPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "UserNotificationPreferences");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserNotificationPreferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Channel = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Digest = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserNotificationPreferences", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_UserNotificationPreferences_TenantId_UserId_Kind_Channel",
                table: "UserNotificationPreferences",
                columns: new[] { "TenantId", "UserId", "Kind", "Channel" },
                unique: true
            );
        }
    }
}
