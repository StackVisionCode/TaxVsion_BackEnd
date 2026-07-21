using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Growth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReferralRefereeBenefit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RefereeBenefitCurrency",
                schema: "referrals",
                table: "ReferralPrograms",
                type: "char(3)",
                nullable: true
            );

            migrationBuilder.AddColumn<int>(
                name: "RefereeBenefitExpirationDays",
                schema: "referrals",
                table: "ReferralPrograms",
                type: "int",
                nullable: false,
                defaultValue: 0
            );

            migrationBuilder.AddColumn<long>(
                name: "RefereeBenefitFixedAmountCents",
                schema: "referrals",
                table: "ReferralPrograms",
                type: "bigint",
                nullable: true
            );

            migrationBuilder.AddColumn<int>(
                name: "RefereeBenefitPercentageBasisPoints",
                schema: "referrals",
                table: "ReferralPrograms",
                type: "int",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "RefereeBenefitType",
                schema: "referrals",
                table: "ReferralPrograms",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "RefereeBenefitCurrency", schema: "referrals", table: "ReferralPrograms");

            migrationBuilder.DropColumn(
                name: "RefereeBenefitExpirationDays",
                schema: "referrals",
                table: "ReferralPrograms"
            );

            migrationBuilder.DropColumn(
                name: "RefereeBenefitFixedAmountCents",
                schema: "referrals",
                table: "ReferralPrograms"
            );

            migrationBuilder.DropColumn(
                name: "RefereeBenefitPercentageBasisPoints",
                schema: "referrals",
                table: "ReferralPrograms"
            );

            migrationBuilder.DropColumn(name: "RefereeBenefitType", schema: "referrals", table: "ReferralPrograms");
        }
    }
}
