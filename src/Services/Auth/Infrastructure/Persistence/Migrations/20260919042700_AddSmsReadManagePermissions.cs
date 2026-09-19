using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSmsReadManagePermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Permissions",
                columns: new[]
                {
                    "Id",
                    "AllowedActorTypes",
                    "Code",
                    "Description",
                    "IsAssignableByTenant",
                    "IsCustomerPortal",
                    "IsDangerous",
                    "MinPlanTier",
                    "Module",
                    "PlatformOnly",
                },
                values: new object[,]
                {
                    {
                        new Guid("a1000000-0000-0000-0000-0000000001f0"),
                        "TenantEmployee,TenantAdmin,PlatformAdmin",
                        "sms.read",
                        "Ver el historial de SMS, su estado y las bajas (opt-outs)",
                        true,
                        false,
                        false,
                        0,
                        "sms",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-0000000001f1"),
                        "TenantEmployee,TenantAdmin,PlatformAdmin",
                        "sms.manage",
                        "Gestionar manualmente las bajas de SMS (opt-out/opt-in)",
                        true,
                        false,
                        false,
                        0,
                        "sms",
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
                keyValue: new Guid("a1000000-0000-0000-0000-0000000001f0")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-0000000001f1")
            );
        }
    }
}
