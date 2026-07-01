using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Tenant.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantDefaultTimeZone : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DefaultTimeZoneId",
                table: "Tenants",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "Etc/UTC");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DefaultTimeZoneId",
                table: "Tenants");
        }
    }
}
