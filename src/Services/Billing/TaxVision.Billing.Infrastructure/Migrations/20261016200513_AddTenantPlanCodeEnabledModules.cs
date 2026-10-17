using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Billing.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantPlanCodeEnabledModules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EnabledModulesJson",
                schema: "billing",
                table: "TenantPlanCodeProjections",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "[]"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EnabledModulesJson",
                schema: "billing",
                table: "TenantPlanCodeProjections"
            );
        }
    }
}
