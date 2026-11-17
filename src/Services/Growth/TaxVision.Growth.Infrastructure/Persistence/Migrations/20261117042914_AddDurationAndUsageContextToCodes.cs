using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Growth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDurationAndUsageContextToCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DurationKind",
                schema: "codes",
                table: "CodeRules",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Once"
            );

            migrationBuilder.AddColumn<int>(
                name: "DurationMonths",
                schema: "codes",
                table: "CodeRules",
                type: "int",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "UsableOn",
                schema: "codes",
                table: "CodeDefinitions",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Signup"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "DurationKind", schema: "codes", table: "CodeRules");

            migrationBuilder.DropColumn(name: "DurationMonths", schema: "codes", table: "CodeRules");

            migrationBuilder.DropColumn(name: "UsableOn", schema: "codes", table: "CodeDefinitions");
        }
    }
}
