using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.PaymentApp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSaaSPaymentReferralBenefitFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CodeReservationId",
                table: "SaaSPayments",
                type: "uniqueidentifier",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "CodeReservationPaymentId",
                table: "SaaSPayments",
                type: "uniqueidentifier",
                nullable: true
            );

            migrationBuilder.AddColumn<long>(
                name: "DiscountAmountCents",
                table: "SaaSPayments",
                type: "bigint",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "PromotionSnapshotHash",
                table: "SaaSPayments",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "CodeReservationId", table: "SaaSPayments");

            migrationBuilder.DropColumn(name: "CodeReservationPaymentId", table: "SaaSPayments");

            migrationBuilder.DropColumn(name: "DiscountAmountCents", table: "SaaSPayments");

            migrationBuilder.DropColumn(name: "PromotionSnapshotHash", table: "SaaSPayments");
        }
    }
}
