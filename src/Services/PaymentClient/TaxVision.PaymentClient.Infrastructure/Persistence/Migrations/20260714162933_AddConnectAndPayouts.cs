using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.PaymentClient.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConnectAndPayouts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "PlatformFeeAmountCents",
                table: "TenantPayments",
                type: "bigint",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "PlatformFeeReference",
                table: "TenantPayments",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "ProviderChargeReferenceOnConnect",
                table: "TenantPayments",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true
            );

            migrationBuilder.AddColumn<long>(
                name: "TenantAmountCents",
                table: "TenantPayments",
                type: "bigint",
                nullable: true
            );

            // Backfill: todo config existente fue creado antes de que Mode existiera (Fase
            // E/F, endpoint sin flujo Connect) — por definición era DirectApiKeys.
            migrationBuilder.AddColumn<string>(
                name: "Mode",
                table: "TenantPaymentConfigs",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "DirectApiKeys"
            );

            migrationBuilder.CreateTable(
                name: "PayoutSchedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantConnectAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Frequency = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Anchor = table.Column<int>(type: "int", nullable: true),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayoutSchedules", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "TenantConnectAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    StripeConnectAccountId = table.Column<string>(
                        type: "nvarchar(255)",
                        maxLength: 255,
                        nullable: false
                    ),
                    AccountType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OnboardingStep = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CanCharge = table.Column<bool>(type: "bit", nullable: false),
                    CanReceivePayouts = table.Column<bool>(type: "bit", nullable: false),
                    RequirementsCurrentlyDue = table.Column<string>(
                        type: "nvarchar(2000)",
                        maxLength: 2000,
                        nullable: false
                    ),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantConnectAccounts", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "PayoutScheduleItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayoutScheduleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderPayoutReference = table.Column<string>(
                        type: "nvarchar(255)",
                        maxLength: 255,
                        nullable: false
                    ),
                    AmountCents = table.Column<long>(type: "bigint", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FailureReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayoutScheduleItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayoutScheduleItems_PayoutSchedules_PayoutScheduleId",
                        column: x => x.PayoutScheduleId,
                        principalTable: "PayoutSchedules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "UX_PayoutScheduleItems_ScheduleId_ProviderReference",
                table: "PayoutScheduleItems",
                columns: new[] { "PayoutScheduleId", "ProviderPayoutReference" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "UX_PayoutSchedules_TenantConnectAccountId",
                table: "PayoutSchedules",
                column: "TenantConnectAccountId",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "UX_TenantConnectAccounts_StripeConnectAccountId",
                table: "TenantConnectAccounts",
                column: "StripeConnectAccountId",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "UX_TenantConnectAccounts_TenantId_ProviderCode",
                table: "TenantConnectAccounts",
                columns: new[] { "TenantId", "ProviderCode" },
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "PayoutScheduleItems");

            migrationBuilder.DropTable(name: "TenantConnectAccounts");

            migrationBuilder.DropTable(name: "PayoutSchedules");

            migrationBuilder.DropColumn(name: "PlatformFeeAmountCents", table: "TenantPayments");

            migrationBuilder.DropColumn(name: "PlatformFeeReference", table: "TenantPayments");

            migrationBuilder.DropColumn(name: "ProviderChargeReferenceOnConnect", table: "TenantPayments");

            migrationBuilder.DropColumn(name: "TenantAmountCents", table: "TenantPayments");

            migrationBuilder.DropColumn(name: "Mode", table: "TenantPaymentConfigs");
        }
    }
}
