using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSignaturePermissions : Migration
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
                        new Guid("a1000000-0000-0000-0000-000000000029"),
                        "signature.request.create",
                        "Crear solicitudes de firma electrónica",
                        false,
                        "signature",
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000030"),
                        "signature.request.read",
                        "Consultar solicitudes de firma",
                        false,
                        "signature",
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000031"),
                        "signature.request.cancel",
                        "Cancelar solicitudes de firma",
                        false,
                        "signature",
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000032"),
                        "signature.request.resend",
                        "Reenviar invitaciones a firmantes",
                        false,
                        "signature",
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000033"),
                        "signature.request.expire",
                        "Extender el vencimiento de solicitudes",
                        false,
                        "signature",
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000034"),
                        "signature.document.prepare",
                        "Validar y preparar documentos para firma",
                        false,
                        "signature",
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000035"),
                        "signature.document.sign",
                        "Aplicar firma del preparador al documento",
                        false,
                        "signature",
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000036"),
                        "signature.document.view",
                        "Ver documentos firmados y sus metadatos",
                        false,
                        "signature",
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000037"),
                        "signature.document.download",
                        "Descargar sellado, original o certificado",
                        false,
                        "signature",
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000038"),
                        "signature.document.audit.read",
                        "Consultar el audit trail de una firma",
                        false,
                        "signature",
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000039"),
                        "signature.template.create",
                        "Crear plantillas de firma reutilizables",
                        false,
                        "signature",
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000040"),
                        "signature.template.update",
                        "Modificar plantillas de firma",
                        false,
                        "signature",
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000041"),
                        "signature.template.delete",
                        "Eliminar plantillas de firma",
                        false,
                        "signature",
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000042"),
                        "signature.settings.manage",
                        "Gestionar la configuración de firma del tenant",
                        false,
                        "signature",
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000043"),
                        "signature.preparer.manage",
                        "Gestionar firmas persistentes del preparador",
                        false,
                        "signature",
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000044"),
                        "signature.certificate.verify",
                        "Verificar certificados de firma (endpoint público)",
                        false,
                        "signature",
                    },
                }
            );

            // Los roles de sistema existentes se alinean con los defaults de PermissionCatalog
            // sin tocar los roles definidos por cada tenant. NOT EXISTS protege el rerun.
            migrationBuilder.Sql(
                """
                INSERT INTO RolePermissions (RoleId, PermissionId)
                SELECT r.Id, p.Id
                FROM Roles AS r
                CROSS JOIN Permissions AS p
                WHERE r.IsSystem = 1
                  AND (
                    (r.Name = N'Tenant Admin' AND p.Code IN (
                      N'signature.request.create',
                      N'signature.request.read',
                      N'signature.request.cancel',
                      N'signature.request.resend',
                      N'signature.request.expire',
                      N'signature.document.prepare',
                      N'signature.document.sign',
                      N'signature.document.view',
                      N'signature.document.download',
                      N'signature.document.audit.read',
                      N'signature.template.create',
                      N'signature.template.update',
                      N'signature.template.delete',
                      N'signature.settings.manage',
                      N'signature.preparer.manage',
                      N'signature.certificate.verify'))
                    OR
                    (r.Name = N'Employee' AND p.Code IN (
                      N'signature.request.create',
                      N'signature.request.read',
                      N'signature.request.resend',
                      N'signature.document.prepare',
                      N'signature.document.sign',
                      N'signature.document.view',
                      N'signature.document.download'))
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
                keyValue: new Guid("a1000000-0000-0000-0000-000000000029")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000030")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000031")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000032")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000033")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000034")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000035")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000036")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000037")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000038")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000039")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000040")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000041")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000042")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000043")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000044")
            );
        }
    }
}
