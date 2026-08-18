using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Postmaster.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBulkRateLimitAndCampaignId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BulkRateLimitPerMinute",
                table: "TenantEmailProviders",
                type: "int",
                nullable: true
            );

            migrationBuilder.AddColumn<int>(
                name: "BulkRateLimitPerMinute",
                table: "SystemEmailProviders",
                type: "int",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "CampaignId",
                table: "SentMessages",
                type: "uniqueidentifier",
                nullable: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_SentMessages_TenantId_CampaignId",
                table: "SentMessages",
                columns: new[] { "TenantId", "CampaignId" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_SentMessages_TenantId_CampaignId", table: "SentMessages");

            migrationBuilder.DropColumn(name: "BulkRateLimitPerMinute", table: "TenantEmailProviders");

            migrationBuilder.DropColumn(name: "BulkRateLimitPerMinute", table: "SystemEmailProviders");

            migrationBuilder.DropColumn(name: "CampaignId", table: "SentMessages");
        }
    }
}
