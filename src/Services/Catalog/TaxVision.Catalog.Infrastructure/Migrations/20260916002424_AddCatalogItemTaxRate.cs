using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Catalog.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogItemTaxRate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TaxRateBasisPoints",
                table: "CatalogItems",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TaxRateBasisPoints",
                table: "CatalogItems");
        }
    }
}
