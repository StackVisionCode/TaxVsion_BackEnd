using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MarkScribeRenderPlatformOnly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000087"),
                column: "PlatformOnly",
                value: true
            );

            // RolePermission rows ya sembrados en EnsureSystemRolesAsync (por tenant, al crearse)
            // no se recomputan solos cuando cambia el catálogo — hay que borrar retroactivamente
            // el grant de scribe.render de cada "Tenant Admin" existente, o el TenantAdmin real
            // seguiría teniendo el claim "perm" en su próximo login pese al cambio de catálogo.
            migrationBuilder.Sql(
                """
                DELETE rp
                FROM RolePermissions rp
                INNER JOIN Roles r ON r.Id = rp.RoleId
                WHERE rp.PermissionId = 'a1000000-0000-0000-0000-000000000087'
                  AND r.Name = 'Tenant Admin';
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000087"),
                column: "PlatformOnly",
                value: false
            );

            // No reinsertamos las filas de RolePermissions borradas en Up: no hay forma de saber
            // si esos tenants las querían o eran solo el bundle automático de SystemRoleDefaults
            // (mismo criterio que las demás migraciones de este catálogo — el Down no reconstruye
            // grants de tenant individuales).
        }
    }
}
