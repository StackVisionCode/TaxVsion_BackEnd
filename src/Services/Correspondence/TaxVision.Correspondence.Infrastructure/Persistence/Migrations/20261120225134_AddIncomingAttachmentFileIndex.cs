using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Correspondence.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIncomingAttachmentFileIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_IncomingEmailAttachments_TenantId_CloudStorageFileId",
                table: "IncomingEmailAttachments",
                columns: new[] { "TenantId", "CloudStorageFileId" },
                filter: "[CloudStorageFileId] IS NOT NULL"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_IncomingEmailAttachments_TenantId_CloudStorageFileId",
                table: "IncomingEmailAttachments"
            );
        }
    }
}
