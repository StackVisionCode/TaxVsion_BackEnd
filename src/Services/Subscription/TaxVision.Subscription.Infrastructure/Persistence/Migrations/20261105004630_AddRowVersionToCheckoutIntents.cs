using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Subscription.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRowVersionToCheckoutIntents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "SubscriptionRenewalIntents",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]
            );

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "SeatPurchaseIntents",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]
            );

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "AddOnPurchaseIntents",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "RowVersion", table: "SubscriptionRenewalIntents");

            migrationBuilder.DropColumn(name: "RowVersion", table: "SeatPurchaseIntents");

            migrationBuilder.DropColumn(name: "RowVersion", table: "AddOnPurchaseIntents");
        }
    }
}
