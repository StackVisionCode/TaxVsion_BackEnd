using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.PaymentApp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddChargebackOutcomeToSaaSPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ChargebackOutcome",
                table: "SaaSPayments",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "ChargebackResolvedAtUtc",
                table: "SaaSPayments",
                type: "datetime2",
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ChargebackOutcome", table: "SaaSPayments");

            migrationBuilder.DropColumn(name: "ChargebackResolvedAtUtc", table: "SaaSPayments");
        }
    }
}
