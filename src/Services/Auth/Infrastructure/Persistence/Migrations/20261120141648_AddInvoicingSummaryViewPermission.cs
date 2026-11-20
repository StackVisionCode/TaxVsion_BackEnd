using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoicingSummaryViewPermission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Permissions",
                columns: new[] { "Id", "AllowedActorTypes", "Code", "Description", "GatedModule", "IsAssignableByTenant", "IsCustomerPortal", "IsDangerous", "IsReserved", "Module", "PlatformOnly" },
                values: new object[] { new Guid("a1000000-0000-0000-0000-000000000230"), "TenantEmployee,TenantAdmin,PlatformAdmin", "invoicing.summary.view", "See the office's billing totals (collected, outstanding, average time to get paid)", null, true, false, false, false, "billing", false });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000230"));
        }
    }
}
