using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCloudStorageSharePermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Permissions",
                columns: new[]
                {
                    "Id",
                    "Code",
                    "Description",
                    "IsAssignableByTenant",
                    "IsCustomerPortal",
                    "MinPlanTier",
                    "Module",
                },
                values: new object[,]
                {
                    {
                        new Guid("a1000000-0000-0000-0000-000000000067"),
                        "cloudstorage.share.create",
                        "Crear links para compartir archivos",
                        true,
                        false,
                        0,
                        "cloudstorage",
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000068"),
                        "cloudstorage.share.revoke",
                        "Revocar links de compartir existentes",
                        true,
                        false,
                        0,
                        "cloudstorage",
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000069"),
                        "cloudstorage.share.manage",
                        "Otorgar permisos elevados en links y gestionar su expiracion",
                        false,
                        false,
                        0,
                        "cloudstorage",
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
                keyValue: new Guid("a1000000-0000-0000-0000-000000000067")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000068")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000069")
            );
        }
    }
}
