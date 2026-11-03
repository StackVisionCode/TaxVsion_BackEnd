using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Scribe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantProfileDisplayNameAndAddress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DisplayName",
                table: "TenantProfileRefs",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "PostalAddress",
                table: "TenantProfileRefs",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true
            );

            migrationBuilder.AddColumn<long>(
                name: "ProfileVersion",
                table: "TenantProfileRefs",
                type: "bigint",
                nullable: false,
                defaultValue: 0L
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "DisplayName", table: "TenantProfileRefs");

            migrationBuilder.DropColumn(name: "PostalAddress", table: "TenantProfileRefs");

            migrationBuilder.DropColumn(name: "ProfileVersion", table: "TenantProfileRefs");
        }
    }
}
