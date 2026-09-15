using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Documents.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantPlanCodeEnabledModules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EnabledModulesJson",
                schema: "documents",
                table: "TenantPlanCodeProjections",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EnabledModulesJson",
                schema: "documents",
                table: "TenantPlanCodeProjections");
        }
    }
}
