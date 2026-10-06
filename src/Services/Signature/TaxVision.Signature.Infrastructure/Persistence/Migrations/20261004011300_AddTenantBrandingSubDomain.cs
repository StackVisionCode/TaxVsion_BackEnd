using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Signature.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantBrandingSubDomain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SubDomain",
                table: "TenantBrandingRefs",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: ""
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "SubDomain", table: "TenantBrandingRefs");
        }
    }
}
