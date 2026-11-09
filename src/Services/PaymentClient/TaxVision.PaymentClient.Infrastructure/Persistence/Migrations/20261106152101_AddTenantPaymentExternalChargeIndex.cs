using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.PaymentClient.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantPaymentExternalChargeIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Q-PCL-6: no se borra ningún pago; la referencia repetida queda solo en el pago más viejo del tenant.
            migrationBuilder.Sql(
                """
                WITH ranked AS (
                    SELECT ExternalChargeProvider, ExternalChargeReference, ROW_NUMBER() OVER (
                        PARTITION BY TenantId, ExternalChargeProvider, ExternalChargeReference ORDER BY CreatedAtUtc, Id) AS rn
                    FROM dbo.TenantPayments WHERE ExternalChargeReference IS NOT NULL
                )
                UPDATE ranked SET ExternalChargeProvider = NULL, ExternalChargeReference = NULL WHERE rn > 1;
                """
            );

            // EF no permite mezclar TenantId con columnas owned en el mapeo: va en SQL.
            migrationBuilder.Sql(
                """
                CREATE UNIQUE INDEX UX_TenantPayments_Tenant_ExternalCharge
                ON dbo.TenantPayments (TenantId, ExternalChargeProvider, ExternalChargeReference)
                WHERE ExternalChargeReference IS NOT NULL;
                """
            );

            migrationBuilder.CreateIndex(
                name: "IX_TenantPayments_ProviderChargeReferenceOnConnect",
                table: "TenantPayments",
                column: "ProviderChargeReferenceOnConnect",
                filter: "[ProviderChargeReferenceOnConnect] IS NOT NULL"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX UX_TenantPayments_Tenant_ExternalCharge ON dbo.TenantPayments;");

            migrationBuilder.DropIndex(
                name: "IX_TenantPayments_ProviderChargeReferenceOnConnect",
                table: "TenantPayments"
            );
        }
    }
}
