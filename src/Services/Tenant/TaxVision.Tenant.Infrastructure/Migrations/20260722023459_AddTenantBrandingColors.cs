using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Tenant.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantBrandingColors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AccentColorHex",
                table: "Tenants",
                type: "nvarchar(7)",
                maxLength: 7,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "BackgroundColorHex",
                table: "Tenants",
                type: "nvarchar(7)",
                maxLength: 7,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "PrimaryColorHex",
                table: "Tenants",
                type: "nvarchar(7)",
                maxLength: 7,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "TextColorHex",
                table: "Tenants",
                type: "nvarchar(7)",
                maxLength: 7,
                nullable: true
            );

            migrationBuilder.UpdateData(
                table: "Tenants",
                keyColumn: "Id",
                keyValue: new Guid("8f58a521-4c25-4d91-9f4e-7ad5df14c001"),
                columns: new[] { "AccentColorHex", "BackgroundColorHex", "PrimaryColorHex", "TextColorHex" },
                values: new object[] { null, null, null, null }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "AccentColorHex", table: "Tenants");

            migrationBuilder.DropColumn(name: "BackgroundColorHex", table: "Tenants");

            migrationBuilder.DropColumn(name: "PrimaryColorHex", table: "Tenants");

            migrationBuilder.DropColumn(name: "TextColorHex", table: "Tenants");
        }
    }
}
