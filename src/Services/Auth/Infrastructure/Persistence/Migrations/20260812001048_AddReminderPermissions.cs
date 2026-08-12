using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReminderPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Permissions",
                columns: new[] { "Id", "AllowedActorTypes", "Code", "Description", "IsAssignableByTenant", "IsCustomerPortal", "IsDangerous", "MinPlanTier", "Module", "PlatformOnly" },
                values: new object[,]
                {
                    { new Guid("a1000000-0000-0000-0000-000000000165"), "TenantEmployee,TenantAdmin,PlatformAdmin", "reminders.read", "Ver los recordatorios propios", true, false, false, 0, "reminders", false },
                    { new Guid("a1000000-0000-0000-0000-000000000166"), "TenantEmployee,TenantAdmin,PlatformAdmin", "reminders.write", "Crear, reprogramar, posponer, descartar y cancelar recordatorios propios", true, false, false, 0, "reminders", false }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000165"));

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000166"));
        }
    }
}
