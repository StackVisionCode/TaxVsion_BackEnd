using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Documents.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentGenerationSignatureCopyOwnerFolder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SignatureCopyFolderType",
                schema: "documents",
                table: "DocumentGenerations",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "SignatureCopyOwnerType",
                schema: "documents",
                table: "DocumentGenerations",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SignatureCopyFolderType",
                schema: "documents",
                table: "DocumentGenerations"
            );

            migrationBuilder.DropColumn(
                name: "SignatureCopyOwnerType",
                schema: "documents",
                table: "DocumentGenerations"
            );
        }
    }
}
