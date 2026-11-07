using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Scribe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSeedContentHashToTemplateVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SeedContentHash",
                table: "EmailTemplateVersions",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SeedContentHash",
                table: "EmailTemplateVersions");
        }
    }
}
