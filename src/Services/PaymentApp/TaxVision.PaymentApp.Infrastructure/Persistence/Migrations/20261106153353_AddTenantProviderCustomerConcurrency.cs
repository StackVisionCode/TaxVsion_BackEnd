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
            // Q-PAY-6: con dos defaults activos queda como default el método más reciente; ninguno se borra.
            migrationBuilder.Sql(
                """
                WITH ranked AS (
                    SELECT IsDefault, ROW_NUMBER() OVER (
                        PARTITION BY TenantProviderCustomerId ORDER BY CreatedAtUtc DESC, Id DESC) AS rn
                    FROM dbo.TenantSavedPaymentMethods WHERE IsDefault = 1 AND IsDetached = 0
                )
                UPDATE ranked SET IsDefault = 0 WHERE rn > 1;
                """
            );

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "TenantProviderCustomers",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]
            );

            migrationBuilder.CreateIndex(
                name: "UX_TenantSavedPaymentMethods_OneDefault",
                table: "TenantSavedPaymentMethods",
                column: "TenantProviderCustomerId",
                unique: true,
                filter: "[IsDefault] = 1 AND [IsDetached] = 0"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_TenantSavedPaymentMethods_OneDefault",
                table: "TenantSavedPaymentMethods"
            );

            migrationBuilder.DropColumn(name: "RowVersion", table: "TenantProviderCustomers");
        }
    }
}
