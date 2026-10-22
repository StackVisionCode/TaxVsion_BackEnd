using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.CloudStorage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOAuthStateProviderAndReturnOrigin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Provider",
                table: "CloudProviderOAuthStates",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: ""
            );

            migrationBuilder.AddColumn<string>(
                name: "ReturnOrigin",
                table: "CloudProviderOAuthStates",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "Provider", table: "CloudProviderOAuthStates");

            migrationBuilder.DropColumn(name: "ReturnOrigin", table: "CloudProviderOAuthStates");
        }
    }
}
