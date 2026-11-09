using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Inventory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStockMovementReferenceIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_Tenant_Reference",
                table: "StockMovements",
                columns: new[] { "TenantId", "Reference" },
                filter: "[Reference] IS NOT NULL"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_StockMovements_Tenant_Reference", table: "StockMovements");
        }
    }
}
