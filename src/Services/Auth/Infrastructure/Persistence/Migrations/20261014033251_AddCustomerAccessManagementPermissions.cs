using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerAccessManagementPermissions : Migration
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
                    "IsReserved",
                    "MinPlanTier",
                    "Module",
                    "PlatformOnly",
                },
                values: new object[,]
                {
                    {
                        new Guid("a1000000-0000-0000-0000-000000000189"),
                        "TenantAdmin,TenantEmployee",
                        "cloudstorage.customer_access.manage_assigned",
                        "Manage document access rules for customers assigned to the user",
                        true,
                        false,
                        false,
                        false,
                        0,
                        "cloudstorage",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000190"),
                        "TenantAdmin,PlatformAdmin",
                        "cloudstorage.customer_access.manage_any",
                        "Manage document access rules for any customer in the tenant",
                        false,
                        false,
                        false,
                        false,
                        0,
                        "cloudstorage",
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
                keyValue: new Guid("a1000000-0000-0000-0000-000000000189")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000190")
            );
        }
    }
}
