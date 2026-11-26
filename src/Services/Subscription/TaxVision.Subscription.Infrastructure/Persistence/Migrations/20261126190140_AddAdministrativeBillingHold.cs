using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Subscription.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAdministrativeBillingHold : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "BillingHeldAtUtc",
                table: "TenantSubscriptions",
                type: "datetime2",
                nullable: true
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "BillingHoldReleasedAtUtc",
                table: "TenantSubscriptions",
                type: "datetime2",
                nullable: true
            );

            migrationBuilder.AddColumn<long>(
                name: "BillingHoldTenantVersion",
                table: "TenantSubscriptions",
                type: "bigint",
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "BillingHeldAtUtc", table: "TenantSubscriptions");

            migrationBuilder.DropColumn(name: "BillingHoldReleasedAtUtc", table: "TenantSubscriptions");

            migrationBuilder.DropColumn(name: "BillingHoldTenantVersion", table: "TenantSubscriptions");
        }
    }
}
