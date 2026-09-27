using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReserveUnbuiltScribeNotificationAndRefundPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000081"),
                columns: new[] { "IsAssignableByTenant", "IsReserved" },
                values: new object[] { false, true }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000085"),
                columns: new[] { "IsAssignableByTenant", "IsReserved" },
                values: new object[] { false, true }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000086"),
                columns: new[] { "IsAssignableByTenant", "IsReserved" },
                values: new object[] { false, true }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000100"),
                columns: new[] { "IsAssignableByTenant", "IsReserved" },
                values: new object[] { false, true }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000101"),
                columns: new[] { "IsAssignableByTenant", "IsReserved" },
                values: new object[] { false, true }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000112"),
                columns: new[] { "IsAssignableByTenant", "IsReserved" },
                values: new object[] { false, true }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000178"),
                columns: new[] { "IsAssignableByTenant", "IsReserved" },
                values: new object[] { false, true }
            );

            // Limpieza de roles CUSTOM, mismo criterio que las migraciones anteriores de A7: estos
            // códigos dejan de ser asignables y no gatean nada (Scribe no tiene campañas ni GET de
            // layouts, Notification retiró su motor de campañas, y no hay endpoint de reembolso),
            // así que quitarlos no le quita a nadie una capacidad real.
            migrationBuilder.Sql(
                """
                DELETE rp
                FROM RolePermissions AS rp
                INNER JOIN Roles AS r ON r.Id = rp.RoleId
                WHERE r.IsSystem = 0
                  AND rp.PermissionId IN (
                      'a1000000-0000-0000-0000-000000000081',
                      'a1000000-0000-0000-0000-000000000085',
                      'a1000000-0000-0000-0000-000000000086',
                      'a1000000-0000-0000-0000-000000000100',
                      'a1000000-0000-0000-0000-000000000101',
                      'a1000000-0000-0000-0000-000000000112',
                      'a1000000-0000-0000-0000-000000000178'
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
                keyValue: new Guid("a1000000-0000-0000-0000-000000000081"),
                columns: new[] { "IsAssignableByTenant", "IsReserved" },
                values: new object[] { true, false }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000085"),
                columns: new[] { "IsAssignableByTenant", "IsReserved" },
                values: new object[] { true, false }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000086"),
                columns: new[] { "IsAssignableByTenant", "IsReserved" },
                values: new object[] { true, false }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000100"),
                columns: new[] { "IsAssignableByTenant", "IsReserved" },
                values: new object[] { true, false }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000101"),
                columns: new[] { "IsAssignableByTenant", "IsReserved" },
                values: new object[] { true, false }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000112"),
                columns: new[] { "IsAssignableByTenant", "IsReserved" },
                values: new object[] { true, false }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000178"),
                columns: new[] { "IsAssignableByTenant", "IsReserved" },
                values: new object[] { true, false }
            );
        }
    }
}
