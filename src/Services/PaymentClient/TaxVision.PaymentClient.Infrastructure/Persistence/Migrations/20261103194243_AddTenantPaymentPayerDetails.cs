using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.PaymentClient.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantPaymentPayerDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CardBrand",
                table: "TenantPayments",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "CardLast4",
                table: "TenantPayments",
                type: "nvarchar(4)",
                maxLength: 4,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "PayerEmail",
                table: "TenantPayments",
                type: "nvarchar(320)",
                maxLength: 320,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "PayerName",
                table: "TenantPayments",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "ReceiptEmail",
                table: "TenantPayments",
                type: "nvarchar(320)",
                maxLength: 320,
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "CardBrand", table: "TenantPayments");

            migrationBuilder.DropColumn(name: "CardLast4", table: "TenantPayments");

            migrationBuilder.DropColumn(name: "PayerEmail", table: "TenantPayments");

            migrationBuilder.DropColumn(name: "PayerName", table: "TenantPayments");

            migrationBuilder.DropColumn(name: "ReceiptEmail", table: "TenantPayments");
        }
    }
}
