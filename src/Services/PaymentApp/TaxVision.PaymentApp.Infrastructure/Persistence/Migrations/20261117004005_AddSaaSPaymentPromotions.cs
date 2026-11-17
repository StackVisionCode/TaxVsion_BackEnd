using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.PaymentApp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSaaSPaymentPromotions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SaaSPaymentPromotions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CodeReservationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DiscountAmountCents = table.Column<long>(type: "bigint", nullable: false),
                    PromotionSnapshotHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Order = table.Column<int>(type: "int", nullable: false),
                    SaaSPaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SaaSPaymentPromotions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SaaSPaymentPromotions_SaaSPayments_SaaSPaymentId",
                        column: x => x.SaaSPaymentId,
                        principalTable: "SaaSPayments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_SaaSPaymentPromotions_CodeReservationId",
                table: "SaaSPaymentPromotions",
                column: "CodeReservationId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_SaaSPaymentPromotions_SaaSPaymentId",
                table: "SaaSPaymentPromotions",
                column: "SaaSPaymentId"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "SaaSPaymentPromotions");
        }
    }
}
