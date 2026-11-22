using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Notification.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PushDeviceSessionAndTokenIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastSeenAtUtc",
                table: "PushDeviceTokens",
                type: "datetime2",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "SessionId",
                table: "PushDeviceTokens",
                type: "uniqueidentifier",
                nullable: true
            );

            migrationBuilder.CreateIndex(name: "IX_PushDeviceTokens_Token", table: "PushDeviceTokens", column: "Token");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_PushDeviceTokens_Token", table: "PushDeviceTokens");

            migrationBuilder.DropColumn(name: "LastSeenAtUtc", table: "PushDeviceTokens");

            migrationBuilder.DropColumn(name: "SessionId", table: "PushDeviceTokens");
        }
    }
}
