using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Notes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNotePlainTextAndFeedIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContentPlainText",
                table: "Notes",
                type: "nvarchar(max)",
                nullable: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_Notes_TenantId_UpdatedAtUtc",
                table: "Notes",
                columns: new[] { "TenantId", "UpdatedAtUtc" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_Notes_TenantId_UpdatedAtUtc", table: "Notes");

            migrationBuilder.DropColumn(name: "ContentPlainText", table: "Notes");
        }
    }
}
