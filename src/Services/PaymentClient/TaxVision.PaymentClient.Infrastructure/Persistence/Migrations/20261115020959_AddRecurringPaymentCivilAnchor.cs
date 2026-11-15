using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.PaymentClient.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRecurringPaymentCivilAnchor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "AnchorDate",
                table: "TenantRecurringPayments",
                type: "date",
                nullable: true
            );

            migrationBuilder.AddColumn<TimeOnly>(
                name: "AnchorLocalTime",
                table: "TenantRecurringPayments",
                type: "time",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "TimeZoneId",
                table: "TenantRecurringPayments",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "AnchorDate", table: "TenantRecurringPayments");

            migrationBuilder.DropColumn(name: "AnchorLocalTime", table: "TenantRecurringPayments");

            migrationBuilder.DropColumn(name: "TimeZoneId", table: "TenantRecurringPayments");
        }
    }
}
