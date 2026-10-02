using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Billing.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIssuerDefaultCurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DefaultCurrency",
                schema: "billing",
                table: "IssuerProfiles",
                type: "nvarchar(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "USD"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "DefaultCurrency", schema: "billing", table: "IssuerProfiles");
        }
    }
}
