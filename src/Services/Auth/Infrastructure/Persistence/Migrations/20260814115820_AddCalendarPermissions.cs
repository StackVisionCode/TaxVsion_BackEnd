using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCalendarPermissions : Migration
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
                        new Guid("a1000000-0000-0000-0000-000000000174"),
                        "TenantEmployee,TenantAdmin,PlatformAdmin",
                        "calendar.read",
                        "Ver el calendario del tenant y consultar disponibilidad",
                        true,
                        false,
                        false,
                        0,
                        "calendar",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000175"),
                        "TenantEmployee,TenantAdmin,PlatformAdmin",
                        "calendar.write",
                        "Crear, mover y cancelar las citas propias",
                        true,
                        false,
                        false,
                        0,
                        "calendar",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000176"),
                        "TenantEmployee,TenantAdmin,PlatformAdmin",
                        "calendar.manage_all",
                        "Reorganizar agendas ajenas actuando como organizador (supervision)",
                        true,
                        false,
                        false,
                        0,
                        "calendar",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000177"),
                        "TenantEmployee,TenantAdmin,PlatformAdmin",
                        "calendar.types.manage",
                        "Definir los tipos de cita de la firma",
                        true,
                        false,
                        false,
                        0,
                        "calendar",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000178"),
                        "TenantEmployee,TenantAdmin,PlatformAdmin",
                        "calendar.availability.manage",
                        "Definir horarios de atencion y bloqueos de agenda",
                        true,
                        false,
                        false,
                        0,
                        "calendar",
                        false,
                    },
                }
            );

            // Los permisos nuevos no llegan solos a los roles ya sembrados: sin esto existen en el
            // catalogo y nadie los tiene, que es como no haberlos creado. Idempotente por el NOT EXISTS.
            migrationBuilder.Sql(
                """
                INSERT INTO RolePermissions (RoleId, PermissionId)
                SELECT r.Id, p.Id
                FROM Roles AS r
                CROSS JOIN Permissions AS p
                WHERE r.IsSystem = 1
                  AND r.Name IN (N'Tenant Admin', N'Employee')
                  AND p.Code = N'calendar.read'
                  AND NOT EXISTS (
                    SELECT 1 FROM RolePermissions AS rp
                    WHERE rp.RoleId = r.Id AND rp.PermissionId = p.Id
                  );
                """
            );
            migrationBuilder.Sql(
                """
                INSERT INTO RolePermissions (RoleId, PermissionId)
                SELECT r.Id, p.Id
                FROM Roles AS r
                CROSS JOIN Permissions AS p
                WHERE r.IsSystem = 1
                  AND r.Name IN (N'Tenant Admin', N'Employee')
                  AND p.Code = N'calendar.write'
                  AND NOT EXISTS (
                    SELECT 1 FROM RolePermissions AS rp
                    WHERE rp.RoleId = r.Id AND rp.PermissionId = p.Id
                  );
                """
            );
            migrationBuilder.Sql(
                """
                INSERT INTO RolePermissions (RoleId, PermissionId)
                SELECT r.Id, p.Id
                FROM Roles AS r
                CROSS JOIN Permissions AS p
                WHERE r.IsSystem = 1
                  AND r.Name IN (N'Tenant Admin')
                  AND p.Code = N'calendar.manage_all'
                  AND NOT EXISTS (
                    SELECT 1 FROM RolePermissions AS rp
                    WHERE rp.RoleId = r.Id AND rp.PermissionId = p.Id
                  );
                """
            );
            migrationBuilder.Sql(
                """
                INSERT INTO RolePermissions (RoleId, PermissionId)
                SELECT r.Id, p.Id
                FROM Roles AS r
                CROSS JOIN Permissions AS p
                WHERE r.IsSystem = 1
                  AND r.Name IN (N'Tenant Admin')
                  AND p.Code = N'calendar.types.manage'
                  AND NOT EXISTS (
                    SELECT 1 FROM RolePermissions AS rp
                    WHERE rp.RoleId = r.Id AND rp.PermissionId = p.Id
                  );
                """
            );
            migrationBuilder.Sql(
                """
                INSERT INTO RolePermissions (RoleId, PermissionId)
                SELECT r.Id, p.Id
                FROM Roles AS r
                CROSS JOIN Permissions AS p
                WHERE r.IsSystem = 1
                  AND r.Name IN (N'Tenant Admin', N'Employee')
                  AND p.Code = N'calendar.availability.manage'
                  AND NOT EXISTS (
                    SELECT 1 FROM RolePermissions AS rp
                    WHERE rp.RoleId = r.Id AND rp.PermissionId = p.Id
                  );
                """
            );

            // Y sembrar las filas TAMPOCO alcanza: las proyecciones locales de cada servicio solo se
            // actualizan por evento. Hasta que Auth arranque y SystemRolePermissionsSyncService las
            // republique, un endpoint con [HasPermission("calendar.*")] responde 403.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DELETE rp FROM RolePermissions AS rp
                JOIN Permissions AS p ON p.Id = rp.PermissionId
                WHERE p.Module = N'calendar';
                """
            );

            migrationBuilder.Sql("DELETE FROM Permissions WHERE Module = N'calendar';");
        }
    }
}
