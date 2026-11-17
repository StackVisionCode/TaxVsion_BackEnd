using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDurationToOnboardingCodeReservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "RecurringAmountCents",
                table: "OnboardingCodeReservations",
                type: "bigint",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "RecurringCodeDefinitionId",
                table: "OnboardingCodeReservations",
                type: "uniqueidentifier",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "RecurringCurrency",
                table: "OnboardingCodeReservations",
                type: "nvarchar(3)",
                maxLength: 3,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "RecurringDuration",
                table: "OnboardingCodeReservations",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true
            );

            migrationBuilder.AddColumn<int>(
                name: "RecurringMonths",
                table: "OnboardingCodeReservations",
                type: "int",
                nullable: true
            );

            migrationBuilder.AddColumn<int>(
                name: "RecurringPercentBasisPoints",
                table: "OnboardingCodeReservations",
                type: "int",
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "RecurringAmountCents", table: "OnboardingCodeReservations");

            migrationBuilder.DropColumn(name: "RecurringCodeDefinitionId", table: "OnboardingCodeReservations");

            migrationBuilder.DropColumn(name: "RecurringCurrency", table: "OnboardingCodeReservations");

            migrationBuilder.DropColumn(name: "RecurringDuration", table: "OnboardingCodeReservations");

            migrationBuilder.DropColumn(name: "RecurringMonths", table: "OnboardingCodeReservations");

            migrationBuilder.DropColumn(name: "RecurringPercentBasisPoints", table: "OnboardingCodeReservations");
        }
    }
}
