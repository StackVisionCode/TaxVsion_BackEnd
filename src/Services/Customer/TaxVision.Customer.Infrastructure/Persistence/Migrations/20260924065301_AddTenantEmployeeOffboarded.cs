using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Customer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantEmployeeOffboarded : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsOffboarded",
                table: "TenantEmployeeDirectoryEntries",
                type: "bit",
                nullable: false,
                defaultValue: false
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "OffboardedAtUtc",
                table: "TenantEmployeeDirectoryEntries",
                type: "datetime2",
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "IsOffboarded", table: "TenantEmployeeDirectoryEntries");

            migrationBuilder.DropColumn(name: "OffboardedAtUtc", table: "TenantEmployeeDirectoryEntries");
        }
    }
}
