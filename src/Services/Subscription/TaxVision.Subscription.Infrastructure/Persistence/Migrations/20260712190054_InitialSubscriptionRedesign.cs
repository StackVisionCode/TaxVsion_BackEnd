using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Subscription.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSubscriptionRedesign : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SubscriptionPlans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Tier = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionPlans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionSeats",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    SourceType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    SourceReferenceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PurchasedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CurrentPeriodStartUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CurrentPeriodEndUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    NextRenewalAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    GracePeriodEndsAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AutoRenew = table.Column<bool>(type: "bit", nullable: false),
                    BillingCycle = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    UnitPriceAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitPriceCurrency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    CancelledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SuspendedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExpiredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancellationReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SuspensionReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionSeats", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionTenantSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AllowAutoRenewTenantSubscription = table.Column<bool>(type: "bit", nullable: false),
                    AllowAutoRenewSeats = table.Column<bool>(type: "bit", nullable: false),
                    AllowSeatSelfAssignment = table.Column<bool>(type: "bit", nullable: false),
                    AllowAdminSeatAssignment = table.Column<bool>(type: "bit", nullable: false),
                    MaxSeatsAllowed = table.Column<int>(type: "int", nullable: true),
                    MinSeatsRequired = table.Column<int>(type: "int", nullable: false),
                    DefaultSeatRenewalDays = table.Column<int>(type: "int", nullable: false),
                    TenantSubscriptionGraceDays = table.Column<int>(type: "int", nullable: false),
                    SeatGraceDays = table.Column<int>(type: "int", nullable: false),
                    AllowSeatReassignment = table.Column<bool>(type: "bit", nullable: false),
                    SeatReassignmentCooldownDays = table.Column<int>(type: "int", nullable: false),
                    AllowAddons = table.Column<bool>(type: "bit", nullable: false),
                    AllowTrial = table.Column<bool>(type: "bit", nullable: false),
                    TrialDays = table.Column<int>(type: "int", nullable: false),
                    SuspendTenantWhenBaseSubscriptionExpired = table.Column<bool>(type: "bit", nullable: false),
                    SuspendUserWhenSeatExpired = table.Column<bool>(type: "bit", nullable: false),
                    NotifyAfterFailedRenewalDays = table.Column<int>(type: "int", nullable: false),
                    AutoRenewCascadeMode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    PauseSeatRenewalsWhenBaseSuspended = table.Column<bool>(type: "bit", nullable: false),
                    PlanChangeEffective = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NotifyBeforeRenewalDays = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionTenantSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionPlanVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionNumber = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    EffectiveFromUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EffectiveUntilUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TrialDaysDefault = table.Column<int>(type: "int", nullable: false),
                    SupportedBillingCycles = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionPlanVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubscriptionPlanVersions_SubscriptionPlans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "SubscriptionPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlanEntitlementDefinitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ValueType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DefaultValue = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanEntitlementDefinitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlanEntitlementDefinitions_SubscriptionPlanVersions_PlanVersionId",
                        column: x => x.PlanVersionId,
                        principalTable: "SubscriptionPlanVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlanFeatures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FeatureKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DefaultEnabled = table.Column<bool>(type: "bit", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanFeatures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlanFeatures_SubscriptionPlanVersions_PlanVersionId",
                        column: x => x.PlanVersionId,
                        principalTable: "SubscriptionPlanVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlanPriceTiers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BillingCycle = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    MinQuantity = table.Column<int>(type: "int", nullable: false),
                    MaxQuantity = table.Column<int>(type: "int", nullable: true),
                    UnitAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanPriceTiers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlanPriceTiers_SubscriptionPlanVersions_PlanVersionId",
                        column: x => x.PlanVersionId,
                        principalTable: "SubscriptionPlanVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TenantSubscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    BillingCycle = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CurrentPeriodStartUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CurrentPeriodEndUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    NextRenewalAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TrialEndsAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    GracePeriodEndsAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SuspendedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExpiredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancellationReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SuspensionReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantSubscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TenantSubscriptions_SubscriptionPlanVersions_PlanVersionId",
                        column: x => x.PlanVersionId,
                        principalTable: "SubscriptionPlanVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TenantSubscriptions_SubscriptionPlans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "SubscriptionPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlanEntitlementDefinitions_PlanVersionId_Key",
                table: "PlanEntitlementDefinitions",
                columns: new[] { "PlanVersionId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlanFeatures_PlanVersionId_FeatureKey",
                table: "PlanFeatures",
                columns: new[] { "PlanVersionId", "FeatureKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlanPriceTiers_PlanVersionId_BillingCycle_MinQuantity",
                table: "PlanPriceTiers",
                columns: new[] { "PlanVersionId", "BillingCycle", "MinQuantity" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPlans_Code",
                table: "SubscriptionPlans",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPlanVersions_PlanId_VersionNumber",
                table: "SubscriptionPlanVersions",
                columns: new[] { "PlanId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_SubscriptionPlanVersions_PlanId_Published",
                table: "SubscriptionPlanVersions",
                column: "PlanId",
                unique: true,
                filter: "[Status] = 'Published'");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionSeats_NextRenewalAtUtc",
                table: "SubscriptionSeats",
                column: "NextRenewalAtUtc",
                filter: "[Status] IN ('Active','PastDue') AND [AutoRenew] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionSeats_TenantId_Status",
                table: "SubscriptionSeats",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionTenantSettings_TenantId",
                table: "SubscriptionTenantSettings",
                column: "TenantId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenantSubscriptions_NextRenewalAtUtc",
                table: "TenantSubscriptions",
                column: "NextRenewalAtUtc",
                filter: "[Status] = 'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_TenantSubscriptions_PlanId",
                table: "TenantSubscriptions",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_TenantSubscriptions_PlanVersionId",
                table: "TenantSubscriptions",
                column: "PlanVersionId");

            migrationBuilder.CreateIndex(
                name: "UX_TenantSubscriptions_TenantId_Active",
                table: "TenantSubscriptions",
                column: "TenantId",
                unique: true,
                filter: "[Status] <> 'Cancelled' AND [Status] <> 'Expired'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlanEntitlementDefinitions");

            migrationBuilder.DropTable(
                name: "PlanFeatures");

            migrationBuilder.DropTable(
                name: "PlanPriceTiers");

            migrationBuilder.DropTable(
                name: "SubscriptionSeats");

            migrationBuilder.DropTable(
                name: "SubscriptionTenantSettings");

            migrationBuilder.DropTable(
                name: "TenantSubscriptions");

            migrationBuilder.DropTable(
                name: "SubscriptionPlanVersions");

            migrationBuilder.DropTable(
                name: "SubscriptionPlans");
        }
    }
}
