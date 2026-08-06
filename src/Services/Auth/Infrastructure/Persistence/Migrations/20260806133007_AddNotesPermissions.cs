using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNotesPermissions : Migration
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
                        new Guid("a1000000-0000-0000-0000-000000000154"),
                        "TenantEmployee,TenantAdmin,PlatformAdmin",
                        "notes.read",
                        "Ver notas del tenant",
                        true,
                        false,
                        false,
                        0,
                        "notes",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000155"),
                        "TenantEmployee,TenantAdmin,PlatformAdmin",
                        "notes.manage",
                        "Crear, editar, archivar/restaurar y adjuntar archivos a notas propias",
                        true,
                        false,
                        false,
                        0,
                        "notes",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000156"),
                        "TenantAdmin,PlatformAdmin",
                        "notes.view_all",
                        "Ver, archivar y borrar notas de cualquier autor del tenant (gobernanza)",
                        true,
                        false,
                        false,
                        0,
                        "notes",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000157"),
                        "CustomerPortal",
                        "notes.portal.read",
                        "El cliente puede ver sus notas marcadas como visibles para el cliente",
                        true,
                        true,
                        false,
                        0,
                        "notes",
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
                keyValue: new Guid("a1000000-0000-0000-0000-000000000154")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000155")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000156")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000157")
            );
        }
    }
}
