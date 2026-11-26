using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MarkSupportAgentPlatformOnly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000049"),
                columns: new[] { "AllowedActorTypes", "IsAssignableByTenant", "PlatformOnly" },
                values: new object[] { "PlatformAdmin", false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000049"),
                columns: new[] { "AllowedActorTypes", "IsAssignableByTenant", "PlatformOnly" },
                values: new object[] { "TenantEmployee,TenantAdmin,PlatformAdmin", true, false });
        }
    }
}
