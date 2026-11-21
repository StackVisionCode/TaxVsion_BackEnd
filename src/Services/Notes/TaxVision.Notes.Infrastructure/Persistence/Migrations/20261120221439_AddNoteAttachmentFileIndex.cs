using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Notes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNoteAttachmentFileIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_NoteAttachments_CloudStorageFileId",
                table: "NoteAttachments",
                column: "CloudStorageFileId"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_NoteAttachments_CloudStorageFileId", table: "NoteAttachments");
        }
    }
}
