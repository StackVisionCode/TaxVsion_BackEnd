using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.PaymentClient.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantPaymentLastProviderCheck : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastProviderCheckAtUtc",
                table: "TenantPayments",
                type: "datetime2",
                nullable: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_TenantPayments_AwaitingProvider",
                table: "TenantPayments",
                columns: new[] { "Status", "LastProviderCheckAtUtc", "UpdatedAtUtc" },
                filter: "[Status] IN ('Processing', 'RequiresAction')"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_TenantPayments_AwaitingProvider", table: "TenantPayments");

            migrationBuilder.DropColumn(name: "LastProviderCheckAtUtc", table: "TenantPayments");
        }
    }
}
