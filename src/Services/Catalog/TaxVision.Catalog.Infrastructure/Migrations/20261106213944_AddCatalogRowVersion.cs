using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Catalog.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogRowVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Categories",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]
            );

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "CatalogItems",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "RowVersion", table: "Categories");

            migrationBuilder.DropColumn(name: "RowVersion", table: "CatalogItems");
        }
    }
}
