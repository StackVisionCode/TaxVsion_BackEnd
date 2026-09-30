using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.PaymentApp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSaaSPaymentTenantHistoryIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_SaaSPayments_TenantId_CreatedAtUtc",
                table: "SaaSPayments",
                columns: new[] { "TenantId", "CreatedAtUtc" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_SaaSPayments_TenantId_CreatedAtUtc", table: "SaaSPayments");
        }
    }
}
