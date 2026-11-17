using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Subscription.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCheckoutProviderToCheckoutIntents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CheckoutProvider",
                table: "SubscriptionSeatRenewals",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "CheckoutProvider",
                table: "SubscriptionRenewalIntents",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "CheckoutProvider",
                table: "SeatPurchaseIntents",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "CheckoutProvider",
                table: "AddOnPurchaseIntents",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "CheckoutProvider", table: "SubscriptionSeatRenewals");

            migrationBuilder.DropColumn(name: "CheckoutProvider", table: "SubscriptionRenewalIntents");

            migrationBuilder.DropColumn(name: "CheckoutProvider", table: "SeatPurchaseIntents");

            migrationBuilder.DropColumn(name: "CheckoutProvider", table: "AddOnPurchaseIntents");
        }
    }
}
