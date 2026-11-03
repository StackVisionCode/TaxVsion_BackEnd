using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.PaymentClient.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantPaymentConfigWebhookSetupRequestedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "WebhookSetupRequestedAtUtc",
                table: "TenantPaymentConfigs",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenantPaymentConfigs_PendingWebhookSetup",
                table: "TenantPaymentConfigs",
                column: "WebhookSetupRequestedAtUtc",
                filter: "[WebhookSetup] = 'Pending'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TenantPaymentConfigs_PendingWebhookSetup",
                table: "TenantPaymentConfigs");

            migrationBuilder.DropColumn(
                name: "WebhookSetupRequestedAtUtc",
                table: "TenantPaymentConfigs");
        }
    }
}
