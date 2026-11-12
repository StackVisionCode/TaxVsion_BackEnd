using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Growth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReferralWalletReward : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ReferralPrograms_PolicyLimits",
                schema: "referrals",
                table: "ReferralPrograms");

            migrationBuilder.AddColumn<long>(
                name: "PricePaidCents",
                schema: "referrals",
                table: "ReferralRewardCases",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "RewardAmountCents",
                schema: "referrals",
                table: "ReferralRewardCases",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "RewardBasisPoints",
                schema: "referrals",
                table: "ReferralRewardCases",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "RewardCapCents",
                schema: "referrals",
                table: "ReferralRewardCases",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RewardCurrency",
                schema: "referrals",
                table: "ReferralRewardCases",
                type: "char(3)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RewardBasisPoints",
                schema: "referrals",
                table: "ReferralPrograms",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "RewardCapCents",
                schema: "referrals",
                table: "ReferralPrograms",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RewardCurrency",
                schema: "referrals",
                table: "ReferralPrograms",
                type: "char(3)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OwnerUserId",
                schema: "referrals",
                table: "ReferralCodes",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Channel",
                schema: "referrals",
                table: "ReferralAttributions",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReferrerUserId",
                schema: "referrals",
                table: "ReferralAttributions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ShareId",
                schema: "referrals",
                table: "ReferralAttributions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ReferralCodeShares",
                schema: "referrals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReferralCodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Channel = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SharedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SharedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    RecipientHint = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReferralCodeShares", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReferralCodeShares_ReferralCodes_ReferralCodeId",
                        column: x => x.ReferralCodeId,
                        principalSchema: "referrals",
                        principalTable: "ReferralCodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReferrerBenefits",
                schema: "referrals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RewardCaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttributionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppliedBasisPoints = table.Column<int>(type: "int", nullable: false),
                    TotalBenefitWalletCents = table.Column<long>(type: "bigint", nullable: false),
                    Currency = table.Column<string>(type: "char(3)", nullable: false),
                    SystemActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreditedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    ReversedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReferrerBenefits", x => x.Id);
                    table.CheckConstraint("CK_ReferrerBenefits_Amounts", "[TotalBenefitWalletCents] > 0 AND [AppliedBasisPoints] BETWEEN 1 AND 10000");
                    table.ForeignKey(
                        name: "FK_ReferrerBenefits_ReferralAttributions_AttributionId",
                        column: x => x.AttributionId,
                        principalSchema: "referrals",
                        principalTable: "ReferralAttributions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReferrerBenefits_ReferralRewardCases_RewardCaseId",
                        column: x => x.RewardCaseId,
                        principalSchema: "referrals",
                        principalTable: "ReferralRewardCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_ReferralPrograms_PolicyLimits",
                schema: "referrals",
                table: "ReferralPrograms",
                sql: "[AttributionWindowDays] > 0 AND [MinimumPaymentAmountCents] >= 0 AND [WaitingPeriodDays] >= 0 AND [MaximumRewardsPerReferrerPerCalendarYear] > 0 AND [RewardBasisPoints] BETWEEN 0 AND 10000");

            migrationBuilder.CreateIndex(
                name: "IX_ReferralCodeShares_ReferralCodeId_SharedAtUtc",
                schema: "referrals",
                table: "ReferralCodeShares",
                columns: new[] { "ReferralCodeId", "SharedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ReferralCodeShares_TenantId_SharedAtUtc",
                schema: "referrals",
                table: "ReferralCodeShares",
                columns: new[] { "TenantId", "SharedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ReferrerBenefits_AttributionId",
                schema: "referrals",
                table: "ReferrerBenefits",
                column: "AttributionId");

            migrationBuilder.CreateIndex(
                name: "IX_ReferrerBenefits_TenantId_CreditedAtUtc",
                schema: "referrals",
                table: "ReferrerBenefits",
                columns: new[] { "TenantId", "CreditedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_ReferrerBenefits_RewardCaseId",
                schema: "referrals",
                table: "ReferrerBenefits",
                column: "RewardCaseId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReferralCodeShares",
                schema: "referrals");

            migrationBuilder.DropTable(
                name: "ReferrerBenefits",
                schema: "referrals");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ReferralPrograms_PolicyLimits",
                schema: "referrals",
                table: "ReferralPrograms");

            migrationBuilder.DropColumn(
                name: "PricePaidCents",
                schema: "referrals",
                table: "ReferralRewardCases");

            migrationBuilder.DropColumn(
                name: "RewardAmountCents",
                schema: "referrals",
                table: "ReferralRewardCases");

            migrationBuilder.DropColumn(
                name: "RewardBasisPoints",
                schema: "referrals",
                table: "ReferralRewardCases");

            migrationBuilder.DropColumn(
                name: "RewardCapCents",
                schema: "referrals",
                table: "ReferralRewardCases");

            migrationBuilder.DropColumn(
                name: "RewardCurrency",
                schema: "referrals",
                table: "ReferralRewardCases");

            migrationBuilder.DropColumn(
                name: "RewardBasisPoints",
                schema: "referrals",
                table: "ReferralPrograms");

            migrationBuilder.DropColumn(
                name: "RewardCapCents",
                schema: "referrals",
                table: "ReferralPrograms");

            migrationBuilder.DropColumn(
                name: "RewardCurrency",
                schema: "referrals",
                table: "ReferralPrograms");

            migrationBuilder.DropColumn(
                name: "OwnerUserId",
                schema: "referrals",
                table: "ReferralCodes");

            migrationBuilder.DropColumn(
                name: "Channel",
                schema: "referrals",
                table: "ReferralAttributions");

            migrationBuilder.DropColumn(
                name: "ReferrerUserId",
                schema: "referrals",
                table: "ReferralAttributions");

            migrationBuilder.DropColumn(
                name: "ShareId",
                schema: "referrals",
                table: "ReferralAttributions");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ReferralPrograms_PolicyLimits",
                schema: "referrals",
                table: "ReferralPrograms",
                sql: "[AttributionWindowDays] > 0 AND [MinimumPaymentAmountCents] >= 0 AND [WaitingPeriodDays] >= 0 AND [MaximumRewardsPerReferrerPerCalendarYear] > 0");
        }
    }
}
