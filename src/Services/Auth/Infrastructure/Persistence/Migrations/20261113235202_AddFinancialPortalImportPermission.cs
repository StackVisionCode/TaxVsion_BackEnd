using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFinancialPortalImportPermission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Permissions",
                columns: new[] { "Id", "AllowedActorTypes", "Code", "Description", "GatedModule", "IsAssignableByTenant", "IsCustomerPortal", "IsDangerous", "IsReserved", "Module", "PlatformOnly" },
                values: new object[] { new Guid("a1000000-0000-0000-0000-0000000001a2"), "CustomerPortal", "financial.portal.import", "The client can submit bank statements for analysis", "financial_insights", true, true, false, false, "financial_insights", false });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-0000000001a2"));
        }
    }
}
