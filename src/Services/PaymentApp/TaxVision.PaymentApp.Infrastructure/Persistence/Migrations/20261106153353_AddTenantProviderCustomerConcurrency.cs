using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.PaymentApp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantProviderCustomerConcurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Con dos defaults activos no se crea el índice: correr Q-PAY-6 y que el dueño elija cuál queda.
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM dbo.TenantSavedPaymentMethods WHERE IsDefault = 1 AND IsDetached = 0
                           GROUP BY TenantProviderCustomerId HAVING COUNT(*) > 1)
                    THROW 50000, 'PAY-6: more than one active default method for a TenantProviderCustomer. Run Q-PAY-6 and resolve each case before applying this migration.', 1;
                """
            );

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "TenantProviderCustomers",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.CreateIndex(
                name: "UX_TenantSavedPaymentMethods_OneDefault",
                table: "TenantSavedPaymentMethods",
                column: "TenantProviderCustomerId",
                unique: true,
                filter: "[IsDefault] = 1 AND [IsDetached] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_TenantSavedPaymentMethods_OneDefault",
                table: "TenantSavedPaymentMethods");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "TenantProviderCustomers");
        }
    }
}
