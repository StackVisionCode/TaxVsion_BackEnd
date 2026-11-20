using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <summary>El desafío MFA recuerda la superficie que lo pidió. Los que ya existen son del login web.</summary>
    public partial class AddMfaChallengeSurface : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Surface",
                table: "MfaChallenges",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Workspace"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "Surface", table: "MfaChallenges");
        }
    }
}
