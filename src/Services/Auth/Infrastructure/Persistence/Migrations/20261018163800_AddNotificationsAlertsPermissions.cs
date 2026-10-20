using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationsAlertsPermissions : Migration
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
                    "GatedModule",
                    "IsAssignableByTenant",
                    "IsCustomerPortal",
                    "IsDangerous",
                    "IsReserved",
                    "MinPlanTier",
                    "Module",
                    "PlatformOnly",
                },
                values: new object[,]
                {
                    {
                        new Guid("a1000000-0000-0000-0000-000000000192"),
                        "TenantEmployee",
                        "notifications.billing.alerts",
                        "Receive office billing alerts (failed payments, payouts, subscription)",
                        null,
                        true,
                        false,
                        false,
                        false,
                        0,
                        "notification",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000193"),
                        "TenantEmployee",
                        "notifications.storage.alerts",
                        "Receive office storage alerts (quota, infected files)",
                        null,
                        true,
                        false,
                        false,
                        false,
                        0,
                        "notification",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000194"),
                        "TenantEmployee",
                        "notifications.security.tenant_alerts",
                        "Receive office security alerts",
                        null,
                        true,
                        false,
                        false,
                        false,
                        0,
                        "notification",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000195"),
                        "TenantEmployee",
                        "notifications.mailbox.alerts",
                        "Receive office mailbox alerts (reconnect, sync issues)",
                        null,
                        true,
                        false,
                        false,
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
                keyValue: new Guid("a1000000-0000-0000-0000-000000000192")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000193")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000194")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000195")
            );
        }
    }
}
