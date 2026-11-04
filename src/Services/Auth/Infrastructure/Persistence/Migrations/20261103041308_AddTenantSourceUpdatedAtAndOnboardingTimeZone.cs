using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantSourceUpdatedAtAndOnboardingTimeZone : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SourceUpdatedAtUtc",
                table: "Tenants",
                type: "datetime2",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "TimeZoneId",
                table: "TenantOnboardings",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true
            );

            migrationBuilder.UpdateData(
                table: "Tenants",
                keyColumn: "Id",
                keyValue: new Guid("8f58a521-4c25-4d91-9f4e-7ad5df14c001"),
                column: "SourceUpdatedAtUtc",
                value: null
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "SourceUpdatedAtUtc", table: "Tenants");

            migrationBuilder.DropColumn(name: "TimeZoneId", table: "TenantOnboardings");
        }
    }
}
