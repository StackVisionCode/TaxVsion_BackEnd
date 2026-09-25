using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MakeSaaSPaymentRefundPlatformOnly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000104"),
                columns: new[] { "AllowedActorTypes", "Description", "IsAssignableByTenant", "PlatformOnly" },
                values: new object[]
                {
                    "PlatformAdmin",
                    "Reembolsar un pago SaaS de cualquier tenant (soporte de plataforma)",
                    false,
                    true,
                }
            );

            // Los custom roles que la tenían la pierden acá; las proyecciones convergen con la
            // reconciliación de permisos. Los roles de sistema los corrige SystemRolePermissionsSyncService
            // al arrancar Auth (publica RolePermissionsChanged).
            migrationBuilder.Sql(
                """
                DELETE rp
                FROM RolePermissions AS rp
                INNER JOIN Roles AS r ON r.Id = rp.RoleId
                WHERE rp.PermissionId = 'a1000000-0000-0000-0000-000000000104' AND r.IsSystem = 0;
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000104"),
                columns: new[] { "AllowedActorTypes", "Description", "IsAssignableByTenant", "PlatformOnly" },
                values: new object[]
                {
                    "TenantEmployee,TenantAdmin,PlatformAdmin",
                    "Reembolsar un pago SaaS del propio tenant",
                    true,
                    false,
                }
            );
        }
    }
}
