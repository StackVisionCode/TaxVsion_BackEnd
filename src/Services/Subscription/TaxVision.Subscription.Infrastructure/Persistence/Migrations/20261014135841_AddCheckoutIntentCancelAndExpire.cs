using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Subscription.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCheckoutIntentCancelAndExpire : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CancelReason",
                table: "SubscriptionRenewalIntents",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAtUtc",
                table: "SubscriptionRenewalIntents",
                type: "datetime2",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "CancelledByUserId",
                table: "SubscriptionRenewalIntents",
                type: "uniqueidentifier",
                nullable: true
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiredAtUtc",
                table: "SubscriptionRenewalIntents",
                type: "datetime2",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "CancelReason",
                table: "SeatPurchaseIntents",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAtUtc",
                table: "SeatPurchaseIntents",
                type: "datetime2",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "CancelledByUserId",
                table: "SeatPurchaseIntents",
                type: "uniqueidentifier",
                nullable: true
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiredAtUtc",
                table: "SeatPurchaseIntents",
                type: "datetime2",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "CancelReason",
                table: "PlanChangeRequests",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAtUtc",
                table: "PlanChangeRequests",
                type: "datetime2",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "CancelledByUserId",
                table: "PlanChangeRequests",
                type: "uniqueidentifier",
                nullable: true
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiredAtUtc",
                table: "PlanChangeRequests",
                type: "datetime2",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "CancelReason",
                table: "AddOnPurchaseIntents",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAtUtc",
                table: "AddOnPurchaseIntents",
                type: "datetime2",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "CancelledByUserId",
                table: "AddOnPurchaseIntents",
                type: "uniqueidentifier",
                nullable: true
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiredAtUtc",
                table: "AddOnPurchaseIntents",
                type: "datetime2",
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "CancelReason", table: "SubscriptionRenewalIntents");

            migrationBuilder.DropColumn(name: "CancelledAtUtc", table: "SubscriptionRenewalIntents");

            migrationBuilder.DropColumn(name: "CancelledByUserId", table: "SubscriptionRenewalIntents");

            migrationBuilder.DropColumn(name: "ExpiredAtUtc", table: "SubscriptionRenewalIntents");

            migrationBuilder.DropColumn(name: "CancelReason", table: "SeatPurchaseIntents");

            migrationBuilder.DropColumn(name: "CancelledAtUtc", table: "SeatPurchaseIntents");

            migrationBuilder.DropColumn(name: "CancelledByUserId", table: "SeatPurchaseIntents");

            migrationBuilder.DropColumn(name: "ExpiredAtUtc", table: "SeatPurchaseIntents");

            migrationBuilder.DropColumn(name: "CancelReason", table: "PlanChangeRequests");

            migrationBuilder.DropColumn(name: "CancelledAtUtc", table: "PlanChangeRequests");

            migrationBuilder.DropColumn(name: "CancelledByUserId", table: "PlanChangeRequests");

            migrationBuilder.DropColumn(name: "ExpiredAtUtc", table: "PlanChangeRequests");

            migrationBuilder.DropColumn(name: "CancelReason", table: "AddOnPurchaseIntents");

            migrationBuilder.DropColumn(name: "CancelledAtUtc", table: "AddOnPurchaseIntents");

            migrationBuilder.DropColumn(name: "CancelledByUserId", table: "AddOnPurchaseIntents");

            migrationBuilder.DropColumn(name: "ExpiredAtUtc", table: "AddOnPurchaseIntents");
        }
    }
}
