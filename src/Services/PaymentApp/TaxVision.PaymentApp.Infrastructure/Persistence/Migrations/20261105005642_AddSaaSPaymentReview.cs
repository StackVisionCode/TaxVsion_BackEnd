using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.PaymentApp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSaaSPaymentReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ReviewPaidAmountCents",
                table: "SaaSPayments",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewPaidCurrency",
                table: "SaaSPayments",
                type: "nvarchar(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewProviderReference",
                table: "SaaSPayments",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewReasonCode",
                table: "SaaSPayments",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReviewRequiredAtUtc",
                table: "SaaSPayments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewResolution",
                table: "SaaSPayments",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReviewResolvedAtUtc",
                table: "SaaSPayments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReviewResolvedByUserId",
                table: "SaaSPayments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SaaSPayments_ReviewPending",
                table: "SaaSPayments",
                column: "ReviewRequiredAtUtc",
                filter: "[ReviewRequiredAtUtc] IS NOT NULL AND [ReviewResolvedAtUtc] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SaaSPayments_ReviewPending",
                table: "SaaSPayments");

            migrationBuilder.DropColumn(
                name: "ReviewPaidAmountCents",
                table: "SaaSPayments");

            migrationBuilder.DropColumn(
                name: "ReviewPaidCurrency",
                table: "SaaSPayments");

            migrationBuilder.DropColumn(
                name: "ReviewProviderReference",
                table: "SaaSPayments");

            migrationBuilder.DropColumn(
                name: "ReviewReasonCode",
                table: "SaaSPayments");

            migrationBuilder.DropColumn(
                name: "ReviewRequiredAtUtc",
                table: "SaaSPayments");

            migrationBuilder.DropColumn(
                name: "ReviewResolution",
                table: "SaaSPayments");

            migrationBuilder.DropColumn(
                name: "ReviewResolvedAtUtc",
                table: "SaaSPayments");

            migrationBuilder.DropColumn(
                name: "ReviewResolvedByUserId",
                table: "SaaSPayments");
        }
    }
}
