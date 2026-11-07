using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Connectors.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountWatchConnectionRowVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "TenantEmailAccounts",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "ProviderWatchSubscriptions",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "OAuthConnections",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "TenantEmailAccounts");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "ProviderWatchSubscriptions");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "OAuthConnections");
        }
    }
}
