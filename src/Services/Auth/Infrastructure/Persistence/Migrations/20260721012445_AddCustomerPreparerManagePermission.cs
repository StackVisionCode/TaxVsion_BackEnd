using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerPreparerManagePermission : Migration
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
                    "PlatformOnly",
                },
                values: new object[]
                {
                    new Guid("a1000000-0000-0000-0000-000000000141"),
                    "customers.preparer.manage",
                    "Asignar o reasignar el preparador responsable de un customer",
                    true,
                    false,
                    0,
                    "customers",
                    false,
                }
            );

            // SystemRoleDefaults ya incluye este permiso para Tenant Admin en tenants NUEVOS
            // (PermissionCatalog.SystemRoleDefaults hace All.Where(!IsCustomerPortal && !PlatformOnly)).
            // Para tenants EXISTENTES hay que otorgarlo explicitamente aca, mismo patron que
            // AddCustomerFiscalProfileRevealPermission. NOT EXISTS protege el rerun.
            migrationBuilder.Sql(
                """
                INSERT INTO RolePermissions (RoleId, PermissionId)
                SELECT r.Id, p.Id
                FROM Roles AS r
                CROSS JOIN Permissions AS p
                WHERE r.IsSystem = 1
                  AND r.Name = N'Tenant Admin'
                  AND p.Code = N'customers.preparer.manage'
                  AND NOT EXISTS (
                    SELECT 1
                    FROM RolePermissions AS rp
                    WHERE rp.RoleId = r.Id AND rp.PermissionId = p.Id
                  );
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000141")
            );
        }
    }
}
