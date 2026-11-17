using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Subscription.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPendingTierChangeToTenantAddOns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PendingAddOnCode",
                table: "TenantAddOns",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "PendingAddOnDefinitionId",
                table: "TenantAddOns",
                type: "uniqueidentifier",
                nullable: true
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "PendingTierChangeRequestedAtUtc",
                table: "TenantAddOns",
                type: "datetime2",
                nullable: true
            );

            migrationBuilder.AddColumn<decimal>(
                name: "PendingUnitPriceAmount",
                table: "TenantAddOns",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "PendingUnitPriceCurrency",
                table: "TenantAddOns",
                type: "nvarchar(3)",
                maxLength: 3,
                nullable: true
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "TierChangeSkippedAtUtc",
                table: "TenantAddOns",
                type: "datetime2",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "TierChangeSkippedReason",
                table: "TenantAddOns",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "UpgradesTenantAddOnId",
                table: "AddOnPurchaseIntents",
                type: "uniqueidentifier",
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "PendingAddOnCode", table: "TenantAddOns");

            migrationBuilder.DropColumn(name: "PendingAddOnDefinitionId", table: "TenantAddOns");

            migrationBuilder.DropColumn(name: "PendingTierChangeRequestedAtUtc", table: "TenantAddOns");

            migrationBuilder.DropColumn(name: "PendingUnitPriceAmount", table: "TenantAddOns");

            migrationBuilder.DropColumn(name: "PendingUnitPriceCurrency", table: "TenantAddOns");

            migrationBuilder.DropColumn(name: "TierChangeSkippedAtUtc", table: "TenantAddOns");

            migrationBuilder.DropColumn(name: "TierChangeSkippedReason", table: "TenantAddOns");

            migrationBuilder.DropColumn(name: "UpgradesTenantAddOnId", table: "AddOnPurchaseIntents");
        }
    }
}
