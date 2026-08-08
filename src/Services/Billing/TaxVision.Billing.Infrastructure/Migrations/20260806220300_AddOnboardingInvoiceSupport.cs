using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Billing.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOnboardingInvoiceSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OnboardingId",
                schema: "billing",
                table: "Invoices",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PaymentId",
                schema: "billing",
                table: "Invoices",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PlanId",
                schema: "billing",
                table: "Invoices",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SettlementType",
                schema: "billing",
                table: "Invoices",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "InvoiceAdjustmentLines",
                schema: "billing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    GrowthReservationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Amount = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceAdjustmentLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvoiceAdjustmentLines_Invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalSchema: "billing",
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_OnboardingId",
                schema: "billing",
                table: "Invoices",
                column: "OnboardingId",
                unique: true,
                filter: "[OnboardingId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceAdjustmentLines_InvoiceId",
                schema: "billing",
                table: "InvoiceAdjustmentLines",
                column: "InvoiceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InvoiceAdjustmentLines",
                schema: "billing");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_OnboardingId",
                schema: "billing",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "OnboardingId",
                schema: "billing",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "PaymentId",
                schema: "billing",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "PlanId",
                schema: "billing",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "SettlementType",
                schema: "billing",
                table: "Invoices");
        }
    }
}
