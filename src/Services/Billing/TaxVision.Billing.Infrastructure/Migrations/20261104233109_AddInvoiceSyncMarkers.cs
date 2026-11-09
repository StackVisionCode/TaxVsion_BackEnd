using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Billing.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceSyncMarkers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PaymentLinkSyncedDue",
                schema: "billing",
                table: "Invoices",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "StockSyncedAtUtc",
                schema: "billing",
                table: "Invoices",
                type: "datetime2",
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "PaymentLinkSyncedDue", schema: "billing", table: "Invoices");

            migrationBuilder.DropColumn(name: "StockSyncedAtUtc", schema: "billing", table: "Invoices");
        }
    }
}
