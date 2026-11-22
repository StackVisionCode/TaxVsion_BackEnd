using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.CloudStorage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFileCategoryAndUsageIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "Files",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true,
                computedColumnSql: "CAST(CASE WHEN LOWER(CASE WHEN CHARINDEX(N'.', [OriginalName]) = 0 THEN [OriginalName] ELSE RIGHT([OriginalName], CHARINDEX(N'.', REVERSE([OriginalName])) - 1) END) IN (N'pdf', N'doc', N'docx', N'xls', N'xlsx', N'ppt', N'pptx', N'txt', N'csv', N'rtf', N'xml', N'json', N'html', N'md') THEN N'Documents' WHEN LOWER(CASE WHEN CHARINDEX(N'.', [OriginalName]) = 0 THEN [OriginalName] ELSE RIGHT([OriginalName], CHARINDEX(N'.', REVERSE([OriginalName])) - 1) END) IN (N'jpg', N'jpeg', N'png', N'gif', N'webp', N'tif', N'tiff', N'bmp', N'svg', N'heic') THEN N'Images' WHEN LOWER(CASE WHEN CHARINDEX(N'.', [OriginalName]) = 0 THEN [OriginalName] ELSE RIGHT([OriginalName], CHARINDEX(N'.', REVERSE([OriginalName])) - 1) END) IN (N'mp3', N'wav', N'm4a', N'ogg', N'aac', N'flac', N'mp4', N'webm', N'mov', N'avi', N'mkv') THEN N'Video & Audio' ELSE N'Others' END AS nvarchar(16))",
                stored: false
            );

            migrationBuilder
                .CreateIndex(
                    name: "IX_Files_Tenant_Status_Category",
                    table: "Files",
                    columns: new[] { "TenantId", "Status", "Category" }
                )
                .Annotation("SqlServer:Include", new[] { "SizeBytes", "StatusBeforeSoftDelete" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_Files_Tenant_Status_Category", table: "Files");

            migrationBuilder.DropColumn(name: "Category", table: "Files");
        }
    }
}
