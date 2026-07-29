using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Billing.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceReceipt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReceiptHash",
                schema: "billing",
                table: "Invoices",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "ReceiptNumber",
                schema: "billing",
                table: "Invoices",
                type: "nvarchar(96)",
                maxLength: 96,
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ReceiptHash", schema: "billing", table: "Invoices");

            migrationBuilder.DropColumn(name: "ReceiptNumber", schema: "billing", table: "Invoices");
        }
    }
}
