using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Subscription.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanChangeRequestCheckout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CheckoutExpiresAtUtc",
                table: "PlanChangeRequests",
                type: "datetime2",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "CheckoutUrl",
                table: "PlanChangeRequests",
                type: "nvarchar(2048)",
                maxLength: 2048,
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "CheckoutExpiresAtUtc", table: "PlanChangeRequests");

            migrationBuilder.DropColumn(name: "CheckoutUrl", table: "PlanChangeRequests");
        }
    }
}
