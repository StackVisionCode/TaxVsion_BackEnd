using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Signature.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantBrandingDisplayName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DisplayName",
                table: "TenantBrandingRefs",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true
            );

            migrationBuilder.AddColumn<long>(
                name: "ProfileVersion",
                table: "TenantBrandingRefs",
                type: "bigint",
                nullable: false,
                defaultValue: 0L
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "DisplayName", table: "TenantBrandingRefs");

            migrationBuilder.DropColumn(name: "ProfileVersion", table: "TenantBrandingRefs");
        }
    }
}
