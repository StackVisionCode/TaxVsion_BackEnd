using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCloudStoragePermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Permissions",
                columns: new[] { "Id", "Code", "Description", "IsCustomerPortal", "Module" },
                values: new object[,]
                {
                    {
                        new Guid("a1000000-0000-0000-0000-000000000023"),
                        "cloudstorage.file.view",
                        "Ver metadatos de archivos",
                        false,
                        "cloudstorage",
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000024"),
                        "cloudstorage.file.upload",
                        "Subir archivos mediante el gateway seguro",
                        false,
                        "cloudstorage",
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000025"),
                        "cloudstorage.file.download",
                        "Descargar archivos disponibles",
                        false,
                        "cloudstorage",
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000026"),
                        "cloudstorage.file.delete",
                        "Eliminar archivos",
                        false,
                        "cloudstorage",
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000027"),
                        "cloudstorage.settings.manage",
                        "Gestionar políticas de almacenamiento",
                        false,
                        "cloudstorage",
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000028"),
                        "cloudstorage.audit.view",
                        "Consultar auditoría de archivos",
                        false,
                        "cloudstorage",
                    },
                }
            );

            // Existing system roles predate this catalog expansion. Keep them aligned
            // with PermissionCatalog defaults without touching tenant-defined roles.
            migrationBuilder.Sql(
                """
                INSERT INTO RolePermissions (RoleId, PermissionId)
                SELECT r.Id, p.Id
                FROM Roles AS r
                CROSS JOIN Permissions AS p
                WHERE r.IsSystem = 1
                  AND (
                    (r.Name = N'Tenant Admin' AND p.Code IN (
                      N'cloudstorage.file.view',
                      N'cloudstorage.file.upload',
                      N'cloudstorage.file.download',
                      N'cloudstorage.file.delete',
                      N'cloudstorage.settings.manage',
                      N'cloudstorage.audit.view'))
                    OR
                    (r.Name IN (N'Employee', N'Customer Portal') AND p.Code IN (
                      N'cloudstorage.file.view',
                      N'cloudstorage.file.upload',
                      N'cloudstorage.file.download'))
                  )
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
                keyValue: new Guid("a1000000-0000-0000-0000-000000000023")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000024")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000025")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000026")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000027")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000028")
            );
        }
    }
}
