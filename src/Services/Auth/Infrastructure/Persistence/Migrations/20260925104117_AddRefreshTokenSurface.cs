using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <summary>Cadena de refresh por superficie dentro de la sesión. Los tokens existentes son del CRM/portal.</summary>
    public partial class AddRefreshTokenSurface : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Surface",
                table: "RefreshTokens",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Workspace"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "Surface", table: "RefreshTokens");
        }
    }
}
