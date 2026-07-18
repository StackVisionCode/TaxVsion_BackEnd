using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Subscription.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanChangeChargeFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ChargeAmountCents",
                table: "PlanChangeRequests",
                type: "bigint",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "ChargeCurrency",
                table: "PlanChangeRequests",
                type: "nvarchar(3)",
                maxLength: 3,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "PaymentIdempotencyKey",
                table: "PlanChangeRequests",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "SaaSPaymentId",
                table: "PlanChangeRequests",
                type: "uniqueidentifier",
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ChargeAmountCents", table: "PlanChangeRequests");

            migrationBuilder.DropColumn(name: "ChargeCurrency", table: "PlanChangeRequests");

            migrationBuilder.DropColumn(name: "PaymentIdempotencyKey", table: "PlanChangeRequests");

            migrationBuilder.DropColumn(name: "SaaSPaymentId", table: "PlanChangeRequests");
        }
    }
}
