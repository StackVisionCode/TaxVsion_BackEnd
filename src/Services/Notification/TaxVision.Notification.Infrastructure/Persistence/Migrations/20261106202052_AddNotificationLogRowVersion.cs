using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Notification.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationLogRowVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "OutboundEmailMessages",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "LastAttemptChangeAtUtc",
                table: "NotificationLogs",
                type: "datetime2",
                nullable: true
            );

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "NotificationLogs",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "RowVersion", table: "OutboundEmailMessages");

            migrationBuilder.DropColumn(name: "LastAttemptChangeAtUtc", table: "NotificationLogs");

            migrationBuilder.DropColumn(name: "RowVersion", table: "NotificationLogs");
        }
    }
}
