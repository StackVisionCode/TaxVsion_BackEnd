using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Billing.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemodelInvoicePaymentLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PayUrl",
                schema: "billing",
                table: "InvoicePaymentLinks");

            migrationBuilder.DropColumn(
                name: "PaymentSource",
                schema: "billing",
                table: "InvoicePaymentLinks");

            migrationBuilder.RenameColumn(
                name: "PaymentId",
                schema: "billing",
                table: "InvoicePaymentLinks",
                newName: "ExternalPayableId");

            migrationBuilder.AddColumn<string>(
                name: "CheckoutUrl",
                schema: "billing",
                table: "InvoicePaymentLinks",
                type: "nvarchar(2048)",
                maxLength: 2048,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiresAtUtc",
                schema: "billing",
                table: "InvoicePaymentLinks",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RevokedAtUtc",
                schema: "billing",
                table: "InvoicePaymentLinks",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_InvoicePaymentLinks_ExternalPayableId",
                schema: "billing",
                table: "InvoicePaymentLinks",
                column: "ExternalPayableId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InvoicePaymentLinks_ExternalPayableId",
                schema: "billing",
                table: "InvoicePaymentLinks");

            migrationBuilder.DropColumn(
                name: "CheckoutUrl",
                schema: "billing",
                table: "InvoicePaymentLinks");

            migrationBuilder.DropColumn(
                name: "ExpiresAtUtc",
                schema: "billing",
                table: "InvoicePaymentLinks");

            migrationBuilder.DropColumn(
                name: "RevokedAtUtc",
                schema: "billing",
                table: "InvoicePaymentLinks");

            migrationBuilder.RenameColumn(
                name: "ExternalPayableId",
                schema: "billing",
                table: "InvoicePaymentLinks",
                newName: "PaymentId");

            migrationBuilder.AddColumn<string>(
                name: "PayUrl",
                schema: "billing",
                table: "InvoicePaymentLinks",
                type: "nvarchar(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentSource",
                schema: "billing",
                table: "InvoicePaymentLinks",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");
        }
    }
}
