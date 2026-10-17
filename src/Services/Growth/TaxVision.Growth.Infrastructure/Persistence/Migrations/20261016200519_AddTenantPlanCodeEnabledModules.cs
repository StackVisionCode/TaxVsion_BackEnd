using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Growth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantPlanCodeEnabledModules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EnabledModulesJson",
                schema: "ratelimiting",
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
                schema: "ratelimiting",
                table: "TenantPlanCodeProjections"
            );
        }
    }
}
