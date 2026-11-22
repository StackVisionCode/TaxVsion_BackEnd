using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Billing.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceDashboardTotals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_Invoices_TenantId_Status", schema: "billing", table: "Invoices");

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                schema: "billing",
                table: "InvoicePayments",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000")
            );

            // Única escritura de datos: cada pago toma la oficina de su factura. Diferido con sp_executesql
            // porque la columna nace en esta misma migración.
            migrationBuilder.Sql(
                "EXEC sp_executesql N'UPDATE p SET p.[TenantId] = i.[TenantId] "
                    + "FROM [billing].[InvoicePayments] AS p INNER JOIN [billing].[Invoices] AS i ON i.[Id] = p.[InvoiceId] "
                    + "WHERE p.[TenantId] = ''00000000-0000-0000-0000-000000000000'';';"
            );

            migrationBuilder.AddColumn<long>(
                name: "AmountDueCents",
                schema: "billing",
                table: "Invoices",
                type: "bigint",
                nullable: false,
                computedColumnSql: "CAST(LEFT([AmountDue], CHARINDEX('|', [AmountDue]) - 1) AS bigint)",
                stored: true
            );

            migrationBuilder.AddColumn<long>(
                name: "AmountPaidCents",
                schema: "billing",
                table: "Invoices",
                type: "bigint",
                nullable: false,
                computedColumnSql: "CAST(LEFT([AmountPaid], CHARINDEX('|', [AmountPaid]) - 1) AS bigint)",
                stored: true
            );

            migrationBuilder.AddColumn<long>(
                name: "TotalCents",
                schema: "billing",
                table: "Invoices",
                type: "bigint",
                nullable: false,
                computedColumnSql: "CAST(LEFT([Total], CHARINDEX('|', [Total]) - 1) AS bigint)",
                stored: true
            );

            migrationBuilder.AddColumn<long>(
                name: "AmountCents",
                schema: "billing",
                table: "InvoicePayments",
                type: "bigint",
                nullable: false,
                computedColumnSql: "CAST(LEFT([Amount], CHARINDEX('|', [Amount]) - 1) AS bigint)",
                stored: true
            );

            migrationBuilder
                .CreateIndex(
                    name: "IX_Invoices_TenantId_CreatedAtUtc",
                    schema: "billing",
                    table: "Invoices",
                    columns: new[] { "TenantId", "CreatedAtUtc" }
                )
                .Annotation("SqlServer:Include", new[] { "Status", "Currency", "CustomerId", "DeletedAtUtc" });

            migrationBuilder
                .CreateIndex(
                    name: "IX_Invoices_TenantId_IssueDateUtc",
                    schema: "billing",
                    table: "Invoices",
                    columns: new[] { "TenantId", "IssueDateUtc" }
                )
                .Annotation(
                    "SqlServer:Include",
                    new[] { "Status", "TotalCents", "Currency", "CustomerId", "DeletedAtUtc" }
                );

            migrationBuilder
                .CreateIndex(
                    name: "IX_Invoices_TenantId_Status",
                    schema: "billing",
                    table: "Invoices",
                    columns: new[] { "TenantId", "Status" }
                )
                .Annotation(
                    "SqlServer:Include",
                    new[]
                    {
                        "AmountDueCents",
                        "DueDateUtc",
                        "IssueDateUtc",
                        "Currency",
                        "CustomerId",
                        "InvoiceNumber",
                        "DeletedAtUtc",
                    }
                );

            migrationBuilder
                .CreateIndex(
                    name: "IX_InvoicePayments_TenantId_PaidAtUtc",
                    schema: "billing",
                    table: "InvoicePayments",
                    columns: new[] { "TenantId", "PaidAtUtc" },
                    filter: "[ReversedAtUtc] IS NULL"
                )
                .Annotation("SqlServer:Include", new[] { "AmountCents", "Source", "InvoiceId", "ReversedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_Invoices_TenantId_CreatedAtUtc", schema: "billing", table: "Invoices");

            migrationBuilder.DropIndex(name: "IX_Invoices_TenantId_IssueDateUtc", schema: "billing", table: "Invoices");

            migrationBuilder.DropIndex(name: "IX_Invoices_TenantId_Status", schema: "billing", table: "Invoices");

            migrationBuilder.DropIndex(
                name: "IX_InvoicePayments_TenantId_PaidAtUtc",
                schema: "billing",
                table: "InvoicePayments"
            );

            migrationBuilder.DropColumn(name: "AmountDueCents", schema: "billing", table: "Invoices");

            migrationBuilder.DropColumn(name: "AmountPaidCents", schema: "billing", table: "Invoices");

            migrationBuilder.DropColumn(name: "TotalCents", schema: "billing", table: "Invoices");

            migrationBuilder.DropColumn(name: "AmountCents", schema: "billing", table: "InvoicePayments");

            migrationBuilder.DropColumn(name: "TenantId", schema: "billing", table: "InvoicePayments");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_TenantId_Status",
                schema: "billing",
                table: "Invoices",
                columns: new[] { "TenantId", "Status" }
            );
        }
    }
}
