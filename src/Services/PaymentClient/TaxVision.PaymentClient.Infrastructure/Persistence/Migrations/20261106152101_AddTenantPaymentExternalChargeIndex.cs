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
            // Con duplicados no se crea el índice: correr Q-PCL-6 y que el dueño resuelva cada caso.
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM dbo.TenantPayments WHERE ExternalChargeReference IS NOT NULL
                           GROUP BY TenantId, ExternalChargeProvider, ExternalChargeReference HAVING COUNT(*) > 1)
                    THROW 50000, 'PCL-6: duplicate (TenantId, ExternalChargeProvider, ExternalChargeReference) in dbo.TenantPayments. Run Q-PCL-6 and resolve each case before applying this migration.', 1;
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
