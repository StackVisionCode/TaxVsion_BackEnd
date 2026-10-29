using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Tasks.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProspectsAndCustomerEmailProjection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LostAtUtc",
                table: "FormParticipants",
                type: "datetime2",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "LostByUserId",
                table: "FormParticipants",
                type: "uniqueidentifier",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "LostReason",
                table: "FormParticipants",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "PrimaryEmailNormalized",
                table: "CustomerDirectoryEntries",
                type: "nvarchar(254)",
                maxLength: 254,
                nullable: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_CustomerDirectoryEntries_TenantId_PrimaryEmail",
                table: "CustomerDirectoryEntries",
                columns: new[] { "TenantId", "PrimaryEmailNormalized" },
                filter: "[PrimaryEmailNormalized] IS NOT NULL"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CustomerDirectoryEntries_TenantId_PrimaryEmail",
                table: "CustomerDirectoryEntries"
            );

            migrationBuilder.DropColumn(name: "LostAtUtc", table: "FormParticipants");

            migrationBuilder.DropColumn(name: "LostByUserId", table: "FormParticipants");

            migrationBuilder.DropColumn(name: "LostReason", table: "FormParticipants");

            migrationBuilder.DropColumn(name: "PrimaryEmailNormalized", table: "CustomerDirectoryEntries");
        }
    }
}
