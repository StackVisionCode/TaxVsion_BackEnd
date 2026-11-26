using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMileageViewPermission : Migration
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
                    "Module",
                    "PlatformOnly",
                },
                values: new object[]
                {
                    new Guid("f49c9c30-7919-4eb8-b3b5-435fef11285e"),
                    "TenantEmployee,TenantAdmin,PlatformAdmin",
                    "mileage.view",
                    "View client mileage trips and routes",
                    "mileage",
                    true,
                    false,
                    false,
                    false,
                    "mobile",
                    false,
                }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("f49c9c30-7919-4eb8-b3b5-435fef11285e")
            );
        }
    }
}
