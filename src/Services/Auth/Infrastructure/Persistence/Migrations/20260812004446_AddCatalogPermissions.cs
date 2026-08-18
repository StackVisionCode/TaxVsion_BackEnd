using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Permissions",
                columns: new[]
                {
                    "Id",
                    "AllowedActorTypes",
                    "Code",
                    "Description",
                    "IsAssignableByTenant",
                    "IsCustomerPortal",
                    "IsDangerous",
                    "MinPlanTier",
                    "Module",
                    "PlatformOnly",
                },
                values: new object[,]
                {
                    {
                        new Guid("a1000000-0000-0000-0000-000000000159"),
                        "TenantEmployee,TenantAdmin,PlatformAdmin",
                        "catalog.read",
                        "Ver el catálogo de productos/servicios",
                        true,
                        false,
                        false,
                        0,
                        "catalog",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000160"),
                        "TenantEmployee,TenantAdmin,PlatformAdmin",
                        "catalog.write",
                        "Crear/editar productos, servicios y categorías",
                        true,
                        false,
                        false,
                        0,
                        "catalog",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000161"),
                        "TenantEmployee,TenantAdmin,PlatformAdmin",
                        "catalog.delete",
                        "Borrar productos, servicios y categorías",
                        true,
                        false,
                        false,
                        0,
                        "catalog",
                        false,
                    },
                }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000159")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000160")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000161")
            );
        }
    }
}
