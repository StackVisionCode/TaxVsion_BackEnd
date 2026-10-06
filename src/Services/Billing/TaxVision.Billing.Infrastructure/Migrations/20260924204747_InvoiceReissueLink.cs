using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Billing.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InvoiceReissueLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "CarriedCreditCents",
                schema: "billing",
                table: "Invoices",
                type: "bigint",
                nullable: false,
                defaultValue: 0L
            );

            migrationBuilder.AddColumn<Guid>(
                name: "ReplacedByInvoiceId",
                schema: "billing",
                table: "Invoices",
                type: "uniqueidentifier",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "ReplacesInvoiceId",
                schema: "billing",
                table: "Invoices",
                type: "uniqueidentifier",
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "CarriedCreditCents", schema: "billing", table: "Invoices");

            migrationBuilder.DropColumn(name: "ReplacedByInvoiceId", schema: "billing", table: "Invoices");

            migrationBuilder.DropColumn(name: "ReplacesInvoiceId", schema: "billing", table: "Invoices");
        }
    }
}
