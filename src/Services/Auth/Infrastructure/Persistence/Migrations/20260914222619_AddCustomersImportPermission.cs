using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomersImportPermission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000102"),
                column: "Description",
                value: "Ver el historial de notificaciones del tenant (email/SMS/in-app) para auditoría y soporte");

            migrationBuilder.InsertData(
                table: "Permissions",
                columns: new[] { "Id", "AllowedActorTypes", "Code", "Description", "IsAssignableByTenant", "IsCustomerPortal", "IsDangerous", "MinPlanTier", "Module", "PlatformOnly" },
                values: new object[] { new Guid("a1000000-0000-0000-0000-00000000009c"), "TenantEmployee,TenantAdmin,PlatformAdmin", "customers.import", "Importar clientes en bloque (CSV/Excel)", true, false, false, 0, "customers", false });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-00000000009c"));

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000102"),
                column: "Description",
                value: "Ver logs de auditoría de Notification del tenant (reservado, sin controller aún)");
        }
    }
}
