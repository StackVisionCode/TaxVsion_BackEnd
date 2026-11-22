using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Notification.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UserDirectoryLanguageZoneName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DisplayName",
                table: "UserEmailDirectoryEntries",
                type: "nvarchar(160)",
                maxLength: 160,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "UserEmailDirectoryEntries",
                type: "nvarchar(5)",
                maxLength: 5,
                nullable: true
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "ProfileSourceAtUtc",
                table: "UserEmailDirectoryEntries",
                type: "datetime2",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "TimeZoneId",
                table: "UserEmailDirectoryEntries",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "DisplayName", table: "UserEmailDirectoryEntries");

            migrationBuilder.DropColumn(name: "Language", table: "UserEmailDirectoryEntries");

            migrationBuilder.DropColumn(name: "ProfileSourceAtUtc", table: "UserEmailDirectoryEntries");

            migrationBuilder.DropColumn(name: "TimeZoneId", table: "UserEmailDirectoryEntries");
        }
    }
}
