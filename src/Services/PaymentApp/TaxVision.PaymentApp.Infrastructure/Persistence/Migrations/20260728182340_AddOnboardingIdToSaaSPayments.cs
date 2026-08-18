using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.PaymentApp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOnboardingIdToSaaSPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OnboardingId",
                table: "SaaSPayments",
                type: "uniqueidentifier",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "ProviderCheckoutSessionId",
                table: "SaaSPayments",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true
            );

            migrationBuilder.CreateIndex(
                name: "UX_SaaSPayments_OnboardingId",
                table: "SaaSPayments",
                column: "OnboardingId",
                unique: true,
                filter: "[OnboardingId] IS NOT NULL"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "UX_SaaSPayments_OnboardingId", table: "SaaSPayments");

            migrationBuilder.DropColumn(name: "OnboardingId", table: "SaaSPayments");

            migrationBuilder.DropColumn(name: "ProviderCheckoutSessionId", table: "SaaSPayments");
        }
    }
}
