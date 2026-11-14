using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFinancialInsightsPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Permissions",
                columns: new[] { "Id", "AllowedActorTypes", "Code", "Description", "GatedModule", "IsAssignableByTenant", "IsCustomerPortal", "IsDangerous", "IsReserved", "Module", "PlatformOnly" },
                values: new object[,]
                {
                    { new Guid("a1000000-0000-0000-0000-0000000001a0"), "TenantEmployee,TenantAdmin,PlatformAdmin", "financial.import", "Register customer bank statement imports", "financial_insights", true, false, false, false, "financial_insights", false },
                    { new Guid("a1000000-0000-0000-0000-0000000001a1"), "TenantEmployee,TenantAdmin,PlatformAdmin", "financial.view", "View customer financial imports", "financial_insights", true, false, false, false, "financial_insights", false }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-0000000001a0"));

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-0000000001a1"));
        }
    }
}
