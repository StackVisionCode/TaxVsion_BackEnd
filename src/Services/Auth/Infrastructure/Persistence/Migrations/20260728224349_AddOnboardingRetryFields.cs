using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOnboardingRetryFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "NextRetryAtUtc",
                table: "TenantOnboardings",
                type: "datetime2",
                nullable: true
            );

            migrationBuilder.AddColumn<int>(
                name: "RetryAttempt",
                table: "TenantOnboardings",
                type: "int",
                nullable: false,
                defaultValue: 0
            );

            migrationBuilder.CreateIndex(
                name: "IX_TenantOnboardings_NextRetryAtUtc",
                table: "TenantOnboardings",
                column: "NextRetryAtUtc",
                filter: "[NextRetryAtUtc] IS NOT NULL"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_TenantOnboardings_NextRetryAtUtc", table: "TenantOnboardings");

            migrationBuilder.DropColumn(name: "NextRetryAtUtc", table: "TenantOnboardings");

            migrationBuilder.DropColumn(name: "RetryAttempt", table: "TenantOnboardings");
        }
    }
}
