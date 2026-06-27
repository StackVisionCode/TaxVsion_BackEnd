using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Tenant.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantKindAndPlatformTenant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "Tenants",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Customer");

            migrationBuilder.InsertData(
                table: "Tenants",
                columns: new[] { "Id", "CreatedAtUtc", "DefaultTimeZoneId", "Kind", "Name", "Status", "SubDomain" },
                values: new object[] { new Guid("8f58a521-4c25-4d91-9f4e-7ad5df14c001"), new DateTime(2026, 6, 27, 0, 0, 0, 0, DateTimeKind.Utc), "Etc/UTC", "Platform", "TaxVision Platform", "Active", "platform-internal" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Tenants",
                keyColumn: "Id",
                keyValue: new Guid("8f58a521-4c25-4d91-9f4e-7ad5df14c001"));

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "Tenants");
        }
    }
}
