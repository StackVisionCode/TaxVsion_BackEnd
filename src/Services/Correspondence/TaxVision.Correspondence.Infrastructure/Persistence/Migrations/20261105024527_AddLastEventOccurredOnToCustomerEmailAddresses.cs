using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Correspondence.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLastEventOccurredOnToCustomerEmailAddresses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastEmailEventOnUtc",
                table: "CustomerEmailAddresses",
                type: "datetime2",
                nullable: true
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "LastStatusEventOnUtc",
                table: "CustomerEmailAddresses",
                type: "datetime2",
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "LastEmailEventOnUtc", table: "CustomerEmailAddresses");

            migrationBuilder.DropColumn(name: "LastStatusEventOnUtc", table: "CustomerEmailAddresses");
        }
    }
}
