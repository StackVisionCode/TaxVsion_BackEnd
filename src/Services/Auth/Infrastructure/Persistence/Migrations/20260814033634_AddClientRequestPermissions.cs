using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddClientRequestPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Permissions",
                columns: new[] { "Id", "AllowedActorTypes", "Code", "Description", "IsAssignableByTenant", "IsCustomerPortal", "IsDangerous", "MinPlanTier", "Module", "PlatformOnly" },
                values: new object[,]
                {
                    { new Guid("a1000000-0000-0000-0000-000000000172"), "TenantEmployee,TenantAdmin,PlatformAdmin", "tasks.client_requests.manage", "Pedirle documentacion al cliente y cerrar lo que mande", true, false, false, 0, "tasks", false },
                    { new Guid("a1000000-0000-0000-0000-000000000173"), "CustomerPortal", "tasks.portal.client_requests", "El cliente ve sus pedidos y registra lo que sube", true, true, false, 0, "tasks", false }
                });

            // Los permisos nuevos no llegan solos a los roles ya sembrados: sin esto existen en el
            // catalogo y nadie los tiene, que es como no haberlos creado. Idempotente por el NOT
            // EXISTS.
            migrationBuilder.Sql(
                """
                INSERT INTO RolePermissions (RoleId, PermissionId)
                SELECT r.Id, p.Id
                FROM Roles AS r
                CROSS JOIN Permissions AS p
                WHERE r.IsSystem = 1
                  AND r.Name IN (N'Tenant Admin', N'Employee')
                  AND p.Code = N'tasks.client_requests.manage'
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
                  AND r.Name = N'Customer Portal'
                  AND p.Code = N'tasks.portal.client_requests'
                  AND NOT EXISTS (
                    SELECT 1 FROM RolePermissions AS rp
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
                keyValue: new Guid("a1000000-0000-0000-0000-000000000172"));

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000173"));
        }
    }
}
