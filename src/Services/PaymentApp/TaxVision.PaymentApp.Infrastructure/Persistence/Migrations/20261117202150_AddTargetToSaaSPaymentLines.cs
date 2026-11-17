using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.PaymentApp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTargetToSaaSPaymentLines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TargetId",
                table: "SaaSPaymentLines",
                type: "uniqueidentifier",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "TargetKind",
                table: "SaaSPaymentLines",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "TargetId", table: "SaaSPaymentLines");

            migrationBuilder.DropColumn(name: "TargetKind", table: "SaaSPaymentLines");
        }
    }
}
