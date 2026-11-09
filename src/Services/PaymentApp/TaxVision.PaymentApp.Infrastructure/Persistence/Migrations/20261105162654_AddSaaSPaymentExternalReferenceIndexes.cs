using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.PaymentApp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSaaSPaymentExternalReferenceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Con duplicados no se crea el índice: correr Q-PAY-4a/b y que el dueño resuelva cada caso.
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM dbo.SaaSPayments WHERE ExternalChargeReference IS NOT NULL
                           GROUP BY ExternalChargeProvider, ExternalChargeReference HAVING COUNT(*) > 1)
                    THROW 50000, 'PAY-4: duplicate (ExternalChargeProvider, ExternalChargeReference) in dbo.SaaSPayments. Run Q-PAY-4a and resolve each case before applying this migration.', 1;
                IF EXISTS (SELECT 1 FROM dbo.SaaSPayments WHERE ProviderCheckoutSessionId IS NOT NULL
                           GROUP BY ProviderCheckoutSessionId HAVING COUNT(*) > 1)
                    THROW 50000, 'PAY-4: duplicate ProviderCheckoutSessionId in dbo.SaaSPayments. Run Q-PAY-4b and resolve each case before applying this migration.', 1;
                """
            );

            migrationBuilder.CreateIndex(
                name: "UX_SaaSPayments_ExternalChargeProvider_Reference",
                table: "SaaSPayments",
                columns: new[] { "ExternalChargeProvider", "ExternalChargeReference" },
                unique: true,
                filter: "[ExternalChargeReference] IS NOT NULL"
            );

            migrationBuilder.CreateIndex(
                name: "UX_SaaSPayments_ProviderCheckoutSessionId",
                table: "SaaSPayments",
                column: "ProviderCheckoutSessionId",
                unique: true,
                filter: "[ProviderCheckoutSessionId] IS NOT NULL"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "UX_SaaSPayments_ExternalChargeProvider_Reference", table: "SaaSPayments");

            migrationBuilder.DropIndex(name: "UX_SaaSPayments_ProviderCheckoutSessionId", table: "SaaSPayments");
        }
    }
}
