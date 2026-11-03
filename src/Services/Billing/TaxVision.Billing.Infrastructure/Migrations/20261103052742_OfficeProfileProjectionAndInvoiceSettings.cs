using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Billing.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OfficeProfileProjectionAndInvoiceSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "SourceVersion",
                schema: "billing",
                table: "IssuerProfiles",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "InvoiceFooter",
                schema: "billing",
                table: "Invoices",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentInstructions",
                schema: "billing",
                table: "Invoices",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentTerms",
                schema: "billing",
                table: "Invoices",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TenantBillingSettings",
                schema: "billing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentTerms = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    PaymentInstructions = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    InvoiceFooter = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantBillingSettings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TenantBillingSettings_TenantId",
                schema: "billing",
                table: "TenantBillingSettings",
                column: "TenantId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TenantBillingSettings",
                schema: "billing");

            migrationBuilder.DropColumn(
                name: "SourceVersion",
                schema: "billing",
                table: "IssuerProfiles");

            migrationBuilder.DropColumn(
                name: "InvoiceFooter",
                schema: "billing",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "PaymentInstructions",
                schema: "billing",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "PaymentTerms",
                schema: "billing",
                table: "Invoices");
        }
    }
}
