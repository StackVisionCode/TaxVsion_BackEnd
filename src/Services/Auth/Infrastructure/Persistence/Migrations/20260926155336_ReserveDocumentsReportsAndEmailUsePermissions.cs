using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReserveDocumentsReportsAndEmailUsePermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000013"),
                columns: new[] { "IsAssignableByTenant", "IsReserved" },
                values: new object[] { false, true }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000014"),
                columns: new[] { "IsAssignableByTenant", "IsReserved" },
                values: new object[] { false, true }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000015"),
                columns: new[] { "IsAssignableByTenant", "IsReserved" },
                values: new object[] { false, true }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000018"),
                columns: new[] { "IsAssignableByTenant", "IsReserved" },
                values: new object[] { false, true }
            );

            // Los cuatro dejan de ser asignables, así que se retiran de los roles CUSTOM que los
            // tuvieran: no gatean nada (los archivos son cloudstorage.file.*, el correo es
            // correspondence.* + el gate del módulo "email", y reportes todavía no existe), así que
            // quitarlos no le quita a nadie una capacidad real — solo limpia el cajón de accesos.
            // Mismo criterio que se aplicó a portal.miles.use.
            migrationBuilder.Sql(
                """
                DELETE rp
                FROM RolePermissions AS rp
                INNER JOIN Roles AS r ON r.Id = rp.RoleId
                WHERE r.IsSystem = 0
                  AND rp.PermissionId IN (
                      'a1000000-0000-0000-0000-000000000013',
                      'a1000000-0000-0000-0000-000000000014',
                      'a1000000-0000-0000-0000-000000000015',
                      'a1000000-0000-0000-0000-000000000018'
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
                keyValue: new Guid("a1000000-0000-0000-0000-000000000013"),
                columns: new[] { "IsAssignableByTenant", "IsReserved" },
                values: new object[] { true, false }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000014"),
                columns: new[] { "IsAssignableByTenant", "IsReserved" },
                values: new object[] { true, false }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000015"),
                columns: new[] { "IsAssignableByTenant", "IsReserved" },
                values: new object[] { true, false }
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000018"),
                columns: new[] { "IsAssignableByTenant", "IsReserved" },
                values: new object[] { true, false }
            );
        }
    }
}
