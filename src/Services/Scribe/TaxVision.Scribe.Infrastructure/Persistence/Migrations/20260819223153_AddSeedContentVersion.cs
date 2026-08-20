using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Scribe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSeedContentVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SeedContentVersion",
                table: "EmailTemplateVersions",
                type: "int",
                nullable: true
            );

            migrationBuilder.AddColumn<int>(
                name: "SeedContentVersion",
                table: "EmailLayoutVersions",
                type: "int",
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "SeedContentVersion", table: "EmailTemplateVersions");

            migrationBuilder.DropColumn(name: "SeedContentVersion", table: "EmailLayoutVersions");
        }
    }
}
