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
            // Q-PAY-4a/b: no se borra ningún pago; la referencia repetida queda solo en el pago más viejo.
            migrationBuilder.Sql(
                """
                WITH ranked AS (
                    SELECT ExternalChargeProvider, ExternalChargeReference, ROW_NUMBER() OVER (
                        PARTITION BY ExternalChargeProvider, ExternalChargeReference ORDER BY CreatedAtUtc, Id) AS rn
                    FROM dbo.SaaSPayments WHERE ExternalChargeReference IS NOT NULL
                )
                UPDATE ranked SET ExternalChargeProvider = NULL, ExternalChargeReference = NULL WHERE rn > 1;

                WITH ranked AS (
                    SELECT ProviderCheckoutSessionId, ROW_NUMBER() OVER (
                        PARTITION BY ProviderCheckoutSessionId ORDER BY CreatedAtUtc, Id) AS rn
                    FROM dbo.SaaSPayments WHERE ProviderCheckoutSessionId IS NOT NULL
                )
                UPDATE ranked SET ProviderCheckoutSessionId = NULL WHERE rn > 1;
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
