using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Tasks.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFormRevisionPdfFileIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_FormSubmissionRevisions_PdfFileId",
                table: "FormSubmissionRevisions",
                column: "PdfFileId",
                filter: "[PdfFileId] IS NOT NULL"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_FormSubmissionRevisions_PdfFileId", table: "FormSubmissionRevisions");
        }
    }
}
