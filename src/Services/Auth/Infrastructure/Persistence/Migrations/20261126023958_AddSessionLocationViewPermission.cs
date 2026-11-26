using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionLocationViewPermission : Migration
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
                    new Guid("9621bed4-df29-4acd-b82c-1a8a48a11391"),
                    "TenantAdmin,TenantEmployee",
                    "users.sessions.location.view",
                    "View team members' session locations",
                    null,
                    true,
                    false,
                    false,
                    false,
                    "users",
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
                keyValue: new Guid("9621bed4-df29-4acd-b82c-1a8a48a11391")
            );
        }
    }
}
