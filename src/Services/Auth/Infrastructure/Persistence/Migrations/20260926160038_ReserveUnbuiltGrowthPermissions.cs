using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReserveUnbuiltGrowthPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000125"),
                columns: new[] { "IsAssignableByTenant", "IsReserved" },
                values: new object[] { false, true }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000128"),
                columns: new[] { "IsAssignableByTenant", "IsReserved" },
                values: new object[] { false, true }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000129"),
                columns: new[] { "IsAssignableByTenant", "IsReserved" },
                values: new object[] { false, true }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000130"),
                column: "IsReserved",
                value: true
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000132"),
                columns: new[] { "IsAssignableByTenant", "IsReserved" },
                values: new object[] { false, true }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000133"),
                columns: new[] { "IsAssignableByTenant", "IsReserved" },
                values: new object[] { false, true }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000134"),
                columns: new[] { "IsAssignableByTenant", "IsReserved" },
                values: new object[] { false, true }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000135"),
                columns: new[] { "IsAssignableByTenant", "IsReserved" },
                values: new object[] { false, true }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000136"),
                column: "IsReserved",
                value: true
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000137"),
                columns: new[] { "IsAssignableByTenant", "IsReserved" },
                values: new object[] { false, true }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000138"),
                column: "IsReserved",
                value: true
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000139"),
                columns: new[] { "IsAssignableByTenant", "IsReserved" },
                values: new object[] { false, true }
            );

            // Ninguno está en un bundle de sistema, así que nadie los tiene por defecto; esto solo
            // limpia los roles CUSTOM que los hubieran recibido cuando aún eran asignables. No quita
            // una capacidad real: no gatean ningún endpoint.
            migrationBuilder.Sql(
                """
                DELETE rp
                FROM RolePermissions AS rp
                INNER JOIN Roles AS r ON r.Id = rp.RoleId
                WHERE r.IsSystem = 0
                  AND rp.PermissionId IN (
                      'a1000000-0000-0000-0000-000000000125',
                      'a1000000-0000-0000-0000-000000000128',
                      'a1000000-0000-0000-0000-000000000129',
                      'a1000000-0000-0000-0000-000000000130',
                      'a1000000-0000-0000-0000-000000000132',
                      'a1000000-0000-0000-0000-000000000133',
                      'a1000000-0000-0000-0000-000000000134',
                      'a1000000-0000-0000-0000-000000000135',
                      'a1000000-0000-0000-0000-000000000136',
                      'a1000000-0000-0000-0000-000000000137',
                      'a1000000-0000-0000-0000-000000000138',
                      'a1000000-0000-0000-0000-000000000139'
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
                keyValue: new Guid("a1000000-0000-0000-0000-000000000125"),
                columns: new[] { "IsAssignableByTenant", "IsReserved" },
                values: new object[] { true, false }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000128"),
                columns: new[] { "IsAssignableByTenant", "IsReserved" },
                values: new object[] { true, false }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000129"),
                columns: new[] { "IsAssignableByTenant", "IsReserved" },
                values: new object[] { true, false }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000130"),
                column: "IsReserved",
                value: false
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000132"),
                columns: new[] { "IsAssignableByTenant", "IsReserved" },
                values: new object[] { true, false }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000133"),
                columns: new[] { "IsAssignableByTenant", "IsReserved" },
                values: new object[] { true, false }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000134"),
                columns: new[] { "IsAssignableByTenant", "IsReserved" },
                values: new object[] { true, false }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000135"),
                columns: new[] { "IsAssignableByTenant", "IsReserved" },
                values: new object[] { true, false }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000136"),
                column: "IsReserved",
                value: false
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000137"),
                columns: new[] { "IsAssignableByTenant", "IsReserved" },
                values: new object[] { true, false }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000138"),
                column: "IsReserved",
                value: false
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000139"),
                columns: new[] { "IsAssignableByTenant", "IsReserved" },
                values: new object[] { true, false }
            );
        }
    }
}
