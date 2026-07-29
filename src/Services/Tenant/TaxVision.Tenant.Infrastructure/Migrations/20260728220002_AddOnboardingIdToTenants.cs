using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Tenant.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOnboardingIdToTenants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OnboardingId",
                table: "Tenants",
                type: "uniqueidentifier",
                nullable: true
            );

            migrationBuilder.UpdateData(
                table: "Tenants",
                keyColumn: "Id",
                keyValue: new Guid("8f58a521-4c25-4d91-9f4e-7ad5df14c001"),
                column: "OnboardingId",
                value: null
            );

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_OnboardingId",
                table: "Tenants",
                column: "OnboardingId",
                unique: true,
                filter: "[OnboardingId] IS NOT NULL"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_Tenants_OnboardingId", table: "Tenants");

            migrationBuilder.DropColumn(name: "OnboardingId", table: "Tenants");
        }
    }
}
