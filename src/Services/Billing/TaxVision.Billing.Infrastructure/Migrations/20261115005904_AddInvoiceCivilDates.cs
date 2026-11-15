using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Billing.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceCivilDates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "DueDate",
                schema: "billing",
                table: "Invoices",
                type: "date",
                nullable: true
            );

            migrationBuilder.AddColumn<DateOnly>(
                name: "IssueDate",
                schema: "billing",
                table: "Invoices",
                type: "date",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "IssueTimeZoneId",
                schema: "billing",
                table: "Invoices",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "DueDate", schema: "billing", table: "Invoices");

            migrationBuilder.DropColumn(name: "IssueDate", schema: "billing", table: "Invoices");

            migrationBuilder.DropColumn(name: "IssueTimeZoneId", schema: "billing", table: "Invoices");
        }
    }
}
