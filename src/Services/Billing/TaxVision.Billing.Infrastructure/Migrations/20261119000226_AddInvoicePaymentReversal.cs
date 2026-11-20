using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Billing.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoicePaymentReversal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReversalReason",
                schema: "billing",
                table: "InvoicePayments",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "ReversedAtUtc",
                schema: "billing",
                table: "InvoicePayments",
                type: "datetime2",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "ReversedByUserId",
                schema: "billing",
                table: "InvoicePayments",
                type: "uniqueidentifier",
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ReversalReason", schema: "billing", table: "InvoicePayments");

            migrationBuilder.DropColumn(name: "ReversedAtUtc", schema: "billing", table: "InvoicePayments");

            migrationBuilder.DropColumn(name: "ReversedByUserId", schema: "billing", table: "InvoicePayments");
        }
    }
}
