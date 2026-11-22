using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Customer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerTenantCreatedAtIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Customers_TenantId_CreatedAtUtc",
                table: "Customers",
                columns: new[] { "TenantId", "CreatedAtUtc" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_Customers_TenantId_CreatedAtUtc", table: "Customers");
        }
    }
}
