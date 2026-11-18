using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Notification.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CustomerDirectorySourceOccurredOn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SourceOccurredOnUtc",
                table: "CustomerEmailDirectoryEntries",
                type: "datetime2",
                nullable: true
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "SourceOccurredOnUtc",
                table: "CustomerContactPolicyProjections",
                type: "datetime2",
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "SourceOccurredOnUtc", table: "CustomerEmailDirectoryEntries");

            migrationBuilder.DropColumn(name: "SourceOccurredOnUtc", table: "CustomerContactPolicyProjections");
        }
    }
}
