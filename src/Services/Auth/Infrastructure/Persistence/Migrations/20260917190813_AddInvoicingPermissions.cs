using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoicingPermissions : Migration
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
                        new Guid("a1000000-0000-0000-0000-000000000180"),
                        "TenantEmployee,TenantAdmin,PlatformAdmin",
                        "invoicing.view",
                        "Ver facturas de clientes del tenant",
                        true,
                        false,
                        false,
                        0,
                        "billing",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000181"),
                        "TenantEmployee,TenantAdmin,PlatformAdmin",
                        "invoicing.manage",
                        "Crear, emitir y gestionar facturas de clientes y los datos del emisor",
                        true,
                        false,
                        false,
                        0,
                        "billing",
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
                keyValue: new Guid("a1000000-0000-0000-0000-000000000180")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000181")
            );
        }
    }
}
