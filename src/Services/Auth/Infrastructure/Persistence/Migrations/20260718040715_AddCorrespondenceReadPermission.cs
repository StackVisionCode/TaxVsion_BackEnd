using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCorrespondenceReadPermission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Permissions",
                columns: new[]
                {
                    "Id",
                    "Code",
                    "Description",
                    "IsAssignableByTenant",
                    "IsCustomerPortal",
                    "MinPlanTier",
                    "Module",
                },
                values: new object[]
                {
                    new Guid("a1000000-0000-0000-0000-000000000072"),
                    "correspondence.read",
                    "Ver la bandeja de correspondencia con customers",
                    true,
                    false,
                    0,
                    "correspondence",
                }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000072")
            );
        }
    }
}
