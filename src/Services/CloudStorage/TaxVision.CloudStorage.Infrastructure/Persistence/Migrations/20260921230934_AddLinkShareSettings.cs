using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.CloudStorage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLinkShareSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowLinkOnlyExternalShares",
                table: "TenantStorageLimits",
                type: "bit",
                nullable: false,
                defaultValue: true
            );

            migrationBuilder.AddColumn<int>(
                name: "MaxShareLifetimeDays",
                table: "TenantStorageLimits",
                type: "int",
                nullable: false,
                defaultValue: 30
            );

            migrationBuilder.AddColumn<bool>(
                name: "RequirePasswordOnLinkShares",
                table: "TenantStorageLimits",
                type: "bit",
                nullable: false,
                defaultValue: false
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "AllowLinkOnlyExternalShares", table: "TenantStorageLimits");

            migrationBuilder.DropColumn(name: "MaxShareLifetimeDays", table: "TenantStorageLimits");

            migrationBuilder.DropColumn(name: "RequirePasswordOnLinkShares", table: "TenantStorageLimits");
        }
    }
}
