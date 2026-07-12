using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Subscription.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEntitlementsAndAddOns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AddOnDefinitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    AllowMultipleInstances = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SupportedBillingCycles = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AddOnDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TenantAddOns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AddOnDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AddOnCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    BillingCycle = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CurrentPeriodStartUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CurrentPeriodEndUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    NextRenewalAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    GracePeriodEndsAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AutoRenew = table.Column<bool>(type: "bit", nullable: false),
                    UnitPriceAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitPriceCurrency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    PurchasedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_TenantAddOns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TenantEntitlementSnapshots",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionNumber = table.Column<long>(type: "bigint", nullable: false),
                    ComputedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PlanCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    PlanVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubscriptionStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    SeatCount = table.Column<int>(type: "int", nullable: false),
                    AvailableSeatCount = table.Column<int>(type: "int", nullable: false),
                    EntriesJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantEntitlementSnapshots", x => x.TenantId);
                });

            migrationBuilder.CreateTable(
                name: "AddOnEntitlementDefinitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AddOnDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ValueType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    MergeStrategy = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AddOnEntitlementDefinitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AddOnEntitlementDefinitions_AddOnDefinitions_AddOnDefinitionId",
                        column: x => x.AddOnDefinitionId,
                        principalTable: "AddOnDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AddOnFeatures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AddOnDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FeatureKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AddOnFeatures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AddOnFeatures_AddOnDefinitions_AddOnDefinitionId",
                        column: x => x.AddOnDefinitionId,
                        principalTable: "AddOnDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AddOnPriceTiers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AddOnDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BillingCycle = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    MinQuantity = table.Column<int>(type: "int", nullable: false),
                    MaxQuantity = table.Column<int>(type: "int", nullable: true),
                    UnitAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AddOnPriceTiers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AddOnPriceTiers_AddOnDefinitions_AddOnDefinitionId",
                        column: x => x.AddOnDefinitionId,
                        principalTable: "AddOnDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AddOnDefinitions_Code",
                table: "AddOnDefinitions",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AddOnEntitlementDefinitions_AddOnDefinitionId_Key",
                table: "AddOnEntitlementDefinitions",
                columns: new[] { "AddOnDefinitionId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AddOnFeatures_AddOnDefinitionId_FeatureKey",
                table: "AddOnFeatures",
                columns: new[] { "AddOnDefinitionId", "FeatureKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AddOnPriceTiers_AddOnDefinitionId_BillingCycle_MinQuantity",
                table: "AddOnPriceTiers",
                columns: new[] { "AddOnDefinitionId", "BillingCycle", "MinQuantity" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenantAddOns_NextRenewalAtUtc",
                table: "TenantAddOns",
                column: "NextRenewalAtUtc",
                filter: "[Status] = 'Active' AND [AutoRenew] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_TenantAddOns_TenantId_Status",
                table: "TenantAddOns",
                columns: new[] { "TenantId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AddOnEntitlementDefinitions");

            migrationBuilder.DropTable(
                name: "AddOnFeatures");

            migrationBuilder.DropTable(
                name: "AddOnPriceTiers");

            migrationBuilder.DropTable(
                name: "TenantAddOns");

            migrationBuilder.DropTable(
                name: "TenantEntitlementSnapshots");

            migrationBuilder.DropTable(
                name: "AddOnDefinitions");
        }
    }
}
