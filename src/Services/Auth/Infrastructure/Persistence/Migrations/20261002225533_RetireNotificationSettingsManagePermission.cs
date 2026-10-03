using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RetireNotificationSettingsManagePermission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000094")
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
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
                    "IsReserved",
                    "MinPlanTier",
                    "Module",
                    "PlatformOnly",
                },
                values: new object[]
                {
                    new Guid("a1000000-0000-0000-0000-000000000094"),
                    "TenantEmployee,TenantAdmin,PlatformAdmin",
                    "notification.settings.manage",
                    "Manage the office's SMTP/API notification settings",
                    true,
                    false,
                    false,
                    false,
                    0,
                    "notification",
                    false,
                }
            );
        }
    }
}
