using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Subscription.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveProrationAddPendingDowngrade : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PlanChangeRequests_EffectiveAtUtc_Pending",
                table: "PlanChangeRequests");

            migrationBuilder.DropColumn(
                name: "PlanChangeEffective",
                table: "SubscriptionTenantSettings");

            migrationBuilder.DropColumn(
                name: "EffectiveAtUtc",
                table: "PlanChangeRequests");

            migrationBuilder.DropColumn(
                name: "EffectiveMode",
                table: "PlanChangeRequests");

            migrationBuilder.RenameColumn(
                name: "CancelledAtUtc",
                table: "PlanChangeRequests",
                newName: "FailedAtUtc");

            migrationBuilder.AlterColumn<string>(
                name: "PaymentIdempotencyKey",
                table: "PlanChangeRequests",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ChargeCurrency",
                table: "PlanChangeRequests",
                type: "nvarchar(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(3)",
                oldMaxLength: 3,
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "ChargeAmountCents",
                table: "PlanChangeRequests",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "PendingDowngrades",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantSubscriptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FromPlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FromPlanVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FromPlanCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ToPlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ToPlanVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ToPlanCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ToBillingCycle = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EffectiveAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AppliedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PendingDowngrades", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PendingDowngrades_TenantSubscriptions_TenantSubscriptionId",
                        column: x => x.TenantSubscriptionId,
                        principalTable: "TenantSubscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PendingDowngrades_EffectiveAtUtc_Scheduled",
                table: "PendingDowngrades",
                column: "EffectiveAtUtc",
                filter: "[Status] = 'Scheduled'");

            migrationBuilder.CreateIndex(
                name: "IX_PendingDowngrades_TenantSubscriptionId_Status",
                table: "PendingDowngrades",
                columns: new[] { "TenantSubscriptionId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PendingDowngrades");

            migrationBuilder.RenameColumn(
                name: "FailedAtUtc",
                table: "PlanChangeRequests",
                newName: "CancelledAtUtc");

            migrationBuilder.AddColumn<string>(
                name: "PlanChangeEffective",
                table: "SubscriptionTenantSettings",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AlterColumn<string>(
                name: "PaymentIdempotencyKey",
                table: "PlanChangeRequests",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "ChargeCurrency",
                table: "PlanChangeRequests",
                type: "nvarchar(3)",
                maxLength: 3,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(3)",
                oldMaxLength: 3);

            migrationBuilder.AlterColumn<long>(
                name: "ChargeAmountCents",
                table: "PlanChangeRequests",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AddColumn<DateTime>(
                name: "EffectiveAtUtc",
                table: "PlanChangeRequests",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "EffectiveMode",
                table: "PlanChangeRequests",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_PlanChangeRequests_EffectiveAtUtc_Pending",
                table: "PlanChangeRequests",
                column: "EffectiveAtUtc",
                filter: "[Status] = 'Pending'");
        }
    }
}
