using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.PaymentApp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantBillingProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BillingAddress",
                table: "Tenants",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "BillingEin",
                table: "Tenants",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "BillingLegalName",
                table: "Tenants",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true
            );

            migrationBuilder.AddColumn<long>(
                name: "ProfileVersion",
                table: "Tenants",
                type: "bigint",
                nullable: false,
                defaultValue: 0L
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "BillingAddress", table: "Tenants");

            migrationBuilder.DropColumn(name: "BillingEin", table: "Tenants");

            migrationBuilder.DropColumn(name: "BillingLegalName", table: "Tenants");

            migrationBuilder.DropColumn(name: "ProfileVersion", table: "Tenants");
        }
    }
}
