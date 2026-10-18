using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Notification.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSeatGateNotificationLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SeatGateNotificationLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NotifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ResolvedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeatGateNotificationLogs", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_SeatGateNotificationLogs_TenantId_UserId",
                table: "SeatGateNotificationLogs",
                columns: new[] { "TenantId", "UserId" },
                unique: true,
                filter: "[ResolvedAtUtc] IS NULL"
            );

            migrationBuilder.CreateIndex(
                name: "IX_SeatGateNotificationLogs_TenantId_UserId_ResolvedAtUtc",
                table: "SeatGateNotificationLogs",
                columns: new[] { "TenantId", "UserId", "ResolvedAtUtc" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "SeatGateNotificationLogs");
        }
    }
}
