using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnforceSignaturePreparerAndExpirePermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000036"),
                columns: new[] { "IsAssignableByTenant", "IsReserved" },
                values: new object[] { false, true }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000037"),
                columns: new[] { "IsAssignableByTenant", "IsReserved" },
                values: new object[] { false, true }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000038"),
                columns: new[] { "IsAssignableByTenant", "IsReserved" },
                values: new object[] { false, true }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000044"),
                columns: new[] { "IsAssignableByTenant", "IsReserved" },
                values: new object[] { false, true }
            );

            // Backfill. Dos endpoints pasan a exigir un permiso propio que antes no pedían, así que un
            // rol CUSTOM que hoy puede hacerlo se quedaría sin poder de un día para otro: administrar la
            // firma persistente del preparador ("My Signature") bastaba con request.create, y extender
            // el vencimiento bastaba con request.resend. Se les concede el permiso nuevo para que nadie
            // pierda lo que ya hacía (§R.7). Los roles de SISTEMA no se tocan por migración: los
            // resincroniza SystemRolePermissionsSyncService al arrancar Auth, que además publica los
            // eventos que las proyecciones necesitan.
            migrationBuilder.Sql(
                """
                INSERT INTO RolePermissions (RoleId, PermissionId)
                SELECT rp.RoleId, CONVERT(uniqueidentifier, 'a1000000-0000-0000-0000-000000000043')
                FROM RolePermissions AS rp
                INNER JOIN Roles AS r ON r.Id = rp.RoleId
                WHERE rp.PermissionId = 'a1000000-0000-0000-0000-000000000029'
                  AND r.IsSystem = 0
                  AND NOT EXISTS (
                      SELECT 1 FROM RolePermissions AS ya
                      WHERE ya.RoleId = rp.RoleId
                        AND ya.PermissionId = CONVERT(uniqueidentifier, 'a1000000-0000-0000-0000-000000000043')
                  );
                """
            );

            migrationBuilder.Sql(
                """
                INSERT INTO RolePermissions (RoleId, PermissionId)
                SELECT rp.RoleId, CONVERT(uniqueidentifier, 'a1000000-0000-0000-0000-000000000033')
                FROM RolePermissions AS rp
                INNER JOIN Roles AS r ON r.Id = rp.RoleId
                WHERE rp.PermissionId = 'a1000000-0000-0000-0000-000000000032'
                  AND r.IsSystem = 0
                  AND NOT EXISTS (
                      SELECT 1 FROM RolePermissions AS ya
                      WHERE ya.RoleId = rp.RoleId
                        AND ya.PermissionId = CONVERT(uniqueidentifier, 'a1000000-0000-0000-0000-000000000033')
                  );
                """
            );

            // Y los cuatro que quedan reservados se retiran de los roles custom que los tuvieran: ya no
            // son asignables y no gatean nada, así que solo ensucian el cajón de accesos. Mismo criterio
            // que se aplicó a portal.miles.use.
            migrationBuilder.Sql(
                """
                DELETE rp
                FROM RolePermissions AS rp
                INNER JOIN Roles AS r ON r.Id = rp.RoleId
                WHERE r.IsSystem = 0
                  AND rp.PermissionId IN (
                      'a1000000-0000-0000-0000-000000000036',
                      'a1000000-0000-0000-0000-000000000037',
                      'a1000000-0000-0000-0000-000000000038',
                      'a1000000-0000-0000-0000-000000000044'
                  );
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000036"),
                columns: new[] { "IsAssignableByTenant", "IsReserved" },
                values: new object[] { true, false }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000037"),
                columns: new[] { "IsAssignableByTenant", "IsReserved" },
                values: new object[] { true, false }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000038"),
                columns: new[] { "IsAssignableByTenant", "IsReserved" },
                values: new object[] { true, false }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000044"),
                columns: new[] { "IsAssignableByTenant", "IsReserved" },
                values: new object[] { true, false }
            );
        }
    }
}
