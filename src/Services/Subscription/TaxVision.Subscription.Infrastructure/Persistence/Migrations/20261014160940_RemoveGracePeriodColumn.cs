using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Subscription.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveGracePeriodColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "GracePeriodEndsAtUtc", table: "TenantSubscriptions");

            migrationBuilder.DropColumn(name: "GracePeriodEndsAtUtc", table: "TenantAddOns");

            migrationBuilder.DropColumn(name: "SeatGraceDays", table: "SubscriptionTenantSettings");

            migrationBuilder.DropColumn(name: "TenantSubscriptionGraceDays", table: "SubscriptionTenantSettings");

            migrationBuilder.DropColumn(name: "GracePeriodEndsAtUtc", table: "SubscriptionSeats");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "GracePeriodEndsAtUtc",
                table: "TenantSubscriptions",
                type: "datetime2",
                nullable: true
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "GracePeriodEndsAtUtc",
                table: "TenantAddOns",
                type: "datetime2",
                nullable: true
            );

            migrationBuilder.AddColumn<int>(
                name: "SeatGraceDays",
                table: "SubscriptionTenantSettings",
                type: "int",
                nullable: false,
                defaultValue: 0
            );

            migrationBuilder.AddColumn<int>(
                name: "TenantSubscriptionGraceDays",
                table: "SubscriptionTenantSettings",
                type: "int",
                nullable: false,
                defaultValue: 0
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "GracePeriodEndsAtUtc",
                table: "SubscriptionSeats",
                type: "datetime2",
                nullable: true
            );
        }
    }
}
