using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Billing.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceVoidFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "VoidReason",
                schema: "billing",
                table: "Invoices",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "VoidedAtUtc",
                schema: "billing",
                table: "Invoices",
                type: "datetime2",
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "VoidReason", schema: "billing", table: "Invoices");

            migrationBuilder.DropColumn(name: "VoidedAtUtc", schema: "billing", table: "Invoices");
        }
    }
}
