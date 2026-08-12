using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Permissions",
                columns: new[] { "Id", "AllowedActorTypes", "Code", "Description", "IsAssignableByTenant", "IsCustomerPortal", "IsDangerous", "MinPlanTier", "Module", "PlatformOnly" },
                values: new object[,]
                {
                    { new Guid("a1000000-0000-0000-0000-000000000162"), "TenantEmployee,TenantAdmin,PlatformAdmin", "inventory.read", "Ver stock, proveedores y movimientos", true, false, false, 0, "inventory", false },
                    { new Guid("a1000000-0000-0000-0000-000000000163"), "TenantEmployee,TenantAdmin,PlatformAdmin", "inventory.write", "Gestionar proveedores y umbrales de stock", true, false, false, 0, "inventory", false },
                    { new Guid("a1000000-0000-0000-0000-000000000164"), "TenantEmployee,TenantAdmin,PlatformAdmin", "inventory.adjust", "Ajustar stock (registrar movimientos)", true, false, false, 0, "inventory", false }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000162"));

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000163"));

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000164"));
        }
    }
}
