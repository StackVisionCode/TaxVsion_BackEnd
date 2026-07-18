using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPostmasterAndNotificationPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Permissions",
                columns: new[]
                {
                    "Id",
                    "Code",
                    "Description",
                    "IsAssignableByTenant",
                    "IsCustomerPortal",
                    "MinPlanTier",
                    "Module",
                    "PlatformOnly",
                },
                values: new object[,]
                {
                    {
                        new Guid("a1000000-0000-0000-0000-000000000089"),
                        "postmaster.messages.read",
                        "Ver el historial de correos enviados del tenant",
                        true,
                        false,
                        0,
                        "postmaster",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000090"),
                        "postmaster.suppression.read",
                        "Ver la suppression list (direcciones que rebotaron o se dieron de baja) del tenant",
                        true,
                        false,
                        0,
                        "postmaster",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000091"),
                        "postmaster.suppression.write",
                        "Agregar o quitar direcciones de la suppression list del tenant",
                        true,
                        false,
                        0,
                        "postmaster",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000092"),
                        "postmaster.providers.read",
                        "Ver el proveedor de correo configurado para el tenant",
                        true,
                        false,
                        0,
                        "postmaster",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000093"),
                        "postmaster.providers.write",
                        "Configurar el proveedor de correo (SMTP/API) del tenant",
                        true,
                        false,
                        0,
                        "postmaster",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000094"),
                        "notification.settings.manage",
                        "Gestionar la configuración SMTP/API de Notification del tenant",
                        true,
                        false,
                        0,
                        "notification",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000095"),
                        "notification.email.send",
                        "Enviar un correo puntual desde Notification",
                        true,
                        false,
                        0,
                        "notification",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000096"),
                        "notification.email.view",
                        "Ver el historial de correos enviados desde Notification",
                        true,
                        false,
                        0,
                        "notification",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000097"),
                        "notification.template.view",
                        "Ver los templates de correo del tenant",
                        true,
                        false,
                        0,
                        "notification",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000098"),
                        "notification.template.manage",
                        "Crear, editar y publicar templates de correo del tenant",
                        true,
                        false,
                        0,
                        "notification",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000099"),
                        "notification.layout.manage",
                        "Gestionar los layouts base de correo del tenant",
                        true,
                        false,
                        0,
                        "notification",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000100"),
                        "notification.campaign.view",
                        "Ver campañas de correo del tenant (reservado, sin controller aún)",
                        true,
                        false,
                        0,
                        "notification",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000101"),
                        "notification.campaign.manage",
                        "Gestionar campañas de correo del tenant (reservado, sin controller aún)",
                        true,
                        false,
                        0,
                        "notification",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000102"),
                        "notification.log.view",
                        "Ver logs de auditoría de Notification del tenant (reservado, sin controller aún)",
                        true,
                        false,
                        0,
                        "notification",
                        false,
                    },
                }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000089")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000090")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000091")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000092")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000093")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000094")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000095")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000096")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000097")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000098")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000099")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000100")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000101")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000102")
            );
        }
    }
}
