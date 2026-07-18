using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerFiscalProfileRevealPermission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Permissions",
                columns: new[] { "Id", "Code", "Description", "IsCustomerPortal", "Module" },
                values: new object[]
                {
                    new Guid("a1000000-0000-0000-0000-000000000063"),
                    "customers.fiscalprofile.reveal",
                    "Revelar el SSN/ITIN/EIN completo de un customer",
                    false,
                    "customers",
                }
            );

            // Solo Tenant Admin, a proposito — a diferencia del resto de permisos de
            // "customers", este NO se agrega a los defaults de Employee (ver
            // PermissionCatalog.SystemRoleDefaults). Es una decision de negocio: un
            // TenantAdmin puede otorgarselo puntualmente a un preparador especifico
            // sin volverlo admin. Los roles de sistema existentes se alinean sin tocar
            // los roles definidos por cada tenant. NOT EXISTS protege el rerun.
            migrationBuilder.Sql(
                """
                INSERT INTO RolePermissions (RoleId, PermissionId)
                SELECT r.Id, p.Id
                FROM Roles AS r
                CROSS JOIN Permissions AS p
                WHERE r.IsSystem = 1
                  AND r.Name = N'Tenant Admin'
                  AND p.Code = N'customers.fiscalprofile.reveal'
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
                keyValue: new Guid("a1000000-0000-0000-0000-000000000063")
            );
        }
    }
}
