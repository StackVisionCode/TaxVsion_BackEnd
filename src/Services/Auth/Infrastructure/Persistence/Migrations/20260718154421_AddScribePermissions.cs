using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddScribePermissions : Migration
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
                },
                values: new object[,]
                {
                    {
                        new Guid("a1000000-0000-0000-0000-000000000079"),
                        "scribe.templates.read",
                        "Ver templates de correo (System y del tenant)",
                        true,
                        false,
                        0,
                        "scribe",
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000080"),
                        "scribe.templates.write",
                        "Crear, editar y publicar versiones de templates de correo",
                        true,
                        false,
                        0,
                        "scribe",
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000081"),
                        "scribe.layouts.read",
                        "Ver layouts de correo (System y del tenant)",
                        true,
                        false,
                        0,
                        "scribe",
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000082"),
                        "scribe.layouts.write",
                        "Crear, editar y publicar versiones de layouts de correo",
                        true,
                        false,
                        0,
                        "scribe",
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000083"),
                        "scribe.event_mappings.read",
                        "Ver las reglas de resolución evento→template",
                        true,
                        false,
                        0,
                        "scribe",
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000084"),
                        "scribe.event_mappings.write",
                        "Crear, editar y borrar reglas de resolución evento→template",
                        true,
                        false,
                        0,
                        "scribe",
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000085"),
                        "scribe.campaigns.read",
                        "Ver campañas de correo basadas en templates de Scribe (reservado, sin controller aún)",
                        true,
                        false,
                        0,
                        "scribe",
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000086"),
                        "scribe.campaigns.write",
                        "Gestionar campañas de correo basadas en templates de Scribe (reservado, sin controller aún)",
                        true,
                        false,
                        0,
                        "scribe",
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000087"),
                        "scribe.render",
                        "Invocar el render de templates (M2M — Notification u otros servicios via token de servicio)",
                        false,
                        false,
                        0,
                        "scribe",
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
                keyValue: new Guid("a1000000-0000-0000-0000-000000000079")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000080")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000081")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000082")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000083")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000084")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000085")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000086")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000087")
            );
        }
    }
}
