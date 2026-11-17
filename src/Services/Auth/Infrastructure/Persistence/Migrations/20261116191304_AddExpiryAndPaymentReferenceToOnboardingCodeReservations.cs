using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExpiryAndPaymentReferenceToOnboardingCodeReservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PricingGeneration",
                table: "TenantOnboardings",
                type: "int",
                nullable: false,
                defaultValue: 0
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiresAtUtc",
                table: "OnboardingCodeReservations",
                type: "datetime2",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "PaymentReferenceId",
                table: "OnboardingCodeReservations",
                type: "uniqueidentifier",
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "PricingGeneration", table: "TenantOnboardings");

            migrationBuilder.DropColumn(name: "ExpiresAtUtc", table: "OnboardingCodeReservations");

            migrationBuilder.DropColumn(name: "PaymentReferenceId", table: "OnboardingCodeReservations");
        }
    }
}
