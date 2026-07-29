using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Subscription.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOnboardingIdToTenantSubscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OnboardingId",
                table: "TenantSubscriptions",
                type: "uniqueidentifier",
                nullable: true
            );

            migrationBuilder.CreateIndex(
                name: "UX_TenantSubscriptions_OnboardingId",
                table: "TenantSubscriptions",
                column: "OnboardingId",
                unique: true,
                filter: "[OnboardingId] IS NOT NULL"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "UX_TenantSubscriptions_OnboardingId", table: "TenantSubscriptions");

            migrationBuilder.DropColumn(name: "OnboardingId", table: "TenantSubscriptions");
        }
    }
}
