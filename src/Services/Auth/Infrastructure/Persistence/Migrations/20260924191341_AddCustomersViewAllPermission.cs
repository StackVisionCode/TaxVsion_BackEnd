using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomersViewAllPermission : Migration
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
                values: new object[]
                {
                    new Guid("a1000000-0000-0000-0000-0000000000c9"),
                    "TenantEmployee,TenantAdmin,PlatformAdmin",
                    "customers.view_all",
                    "Ver TODOS los clientes del tenant (no solo los asignados)",
                    true,
                    false,
                    false,
                    0,
                    "customers",
                    false,
                }
            );

            // NO se otorga customers.view_all a los roles 'Tenant Admin' aquí a propósito. El grant a los
            // roles de sistema (y su propagación a las proyecciones downstream) lo hace en el arranque
            // SystemRolePermissionsSyncService: recomputa el set de cada rol de sistema contra el catálogo
            // (SystemTenantAdminRootPermissions incluye este permiso por ser no-portal/no-platform), lo
            // concede y PUBLICA RolePermissionsChanged con la versión bumpeada. Hacer el INSERT manual acá
            // (como hacía antes) dejaba Auth "ya correcto" → el sync no veía diferencia → NO publicaba el
            // evento → las proyecciones (Customer/SMS/Signature/…) quedaban sin el permiso (admin veía 0
            // clientes con la visibilidad por asignación encendida). Solo se siembra el permiso en el catálogo.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Quita los grants antes de borrar el permiso (FK RolePermissions → Permissions).
            migrationBuilder.Sql(
                """
                DELETE rp
                FROM RolePermissions AS rp
                INNER JOIN Permissions AS p ON p.Id = rp.PermissionId
                WHERE p.Code = N'customers.view_all';
                """
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-0000000000c9")
            );
        }
    }
}
