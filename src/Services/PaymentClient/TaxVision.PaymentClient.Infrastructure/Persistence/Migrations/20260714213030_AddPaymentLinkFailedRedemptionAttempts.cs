using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.PaymentClient.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentLinkFailedRedemptionAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FailedRedemptionAttempts",
                table: "PaymentLinks",
                type: "int",
                nullable: false,
                defaultValue: 0
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "FailedRedemptionAttempts", table: "PaymentLinks");
        }
    }
}
