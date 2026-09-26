using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.PaymentApp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSaaSPaymentChargeBreakdown : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BreakdownQuantity",
                table: "SaaSPayments",
                type: "int",
                nullable: true
            );

            migrationBuilder.AddColumn<long>(
                name: "BreakdownUnitAmountCents",
                table: "SaaSPayments",
                type: "bigint",
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "BreakdownQuantity", table: "SaaSPayments");

            migrationBuilder.DropColumn(name: "BreakdownUnitAmountCents", table: "SaaSPayments");
        }
    }
}
