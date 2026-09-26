using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.PaymentApp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSaaSPaymentReceiptFileId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ReceiptFileId",
                table: "SaaSPayments",
                type: "uniqueidentifier",
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ReceiptFileId", table: "SaaSPayments");
        }
    }
}
