using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Billing.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TuneDashboardSummaryIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_Invoices_TenantId_Status", schema: "billing", table: "Invoices");

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
                        "AmountDue",
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
                    name: "IX_CustomerAssignmentProjections_TenantId_UserId",
                    schema: "billing",
                    table: "CustomerAssignmentProjections",
                    columns: new[] { "TenantId", "UserId" }
                )
                .Annotation("SqlServer:Include", new[] { "CustomerId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_Invoices_TenantId_Status", schema: "billing", table: "Invoices");

            migrationBuilder.DropIndex(
                name: "IX_CustomerAssignmentProjections_TenantId_UserId",
                schema: "billing",
                table: "CustomerAssignmentProjections"
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
        }
    }
}
