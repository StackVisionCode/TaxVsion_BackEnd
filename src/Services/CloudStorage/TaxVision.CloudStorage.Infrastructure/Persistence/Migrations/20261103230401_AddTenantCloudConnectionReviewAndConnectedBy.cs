using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.CloudStorage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantCloudConnectionReviewAndConnectedBy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ConnectedByUserId",
                table: "TenantCloudConnections",
                type: "uniqueidentifier",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "ReviewReason",
                table: "TenantCloudConnections",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "ReviewRequiredAtUtc",
                table: "TenantCloudConnections",
                type: "datetime2",
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ConnectedByUserId", table: "TenantCloudConnections");

            migrationBuilder.DropColumn(name: "ReviewReason", table: "TenantCloudConnections");

            migrationBuilder.DropColumn(name: "ReviewRequiredAtUtc", table: "TenantCloudConnections");
        }
    }
}
