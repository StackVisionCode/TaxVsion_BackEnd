using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Inventory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStockLevelCatalogStateAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CatalogStateAtUtc",
                table: "StockLevels",
                type: "datetime2",
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "CatalogStateAtUtc", table: "StockLevels");
        }
    }
}
