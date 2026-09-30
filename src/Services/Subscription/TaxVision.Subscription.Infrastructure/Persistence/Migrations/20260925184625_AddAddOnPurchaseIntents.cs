using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Subscription.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAddOnPurchaseIntents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AddOnPurchaseIntents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AddOnDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AddOnCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    AutoRenew = table.Column<bool>(type: "bit", nullable: false),
                    UnitPriceAmount = table.Column<decimal>(
                        type: "decimal(18,4)",
                        precision: 18,
                        scale: 4,
                        nullable: false
                    ),
                    UnitPriceCurrency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    BillingCycle = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ProratedTotalCents = table.Column<long>(type: "bigint", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SaaSPaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CheckoutUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    CheckoutExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantAddOnId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RequestedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PaidAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FailureReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AddOnPurchaseIntents", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_AddOnPurchaseIntents_SaaSPaymentId",
                table: "AddOnPurchaseIntents",
                column: "SaaSPaymentId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_AddOnPurchaseIntents_TenantId",
                table: "AddOnPurchaseIntents",
                column: "TenantId"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "AddOnPurchaseIntents");
        }
    }
}
