using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Sms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSmsMessageSendAttemptAndWalletCharged : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SendAttemptedAtUtc",
                table: "smsMessages",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "WalletCharged",
                table: "smsMessages",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SendAttemptedAtUtc",
                table: "smsMessages");

            migrationBuilder.DropColumn(
                name: "WalletCharged",
                table: "smsMessages");
        }
    }
}
