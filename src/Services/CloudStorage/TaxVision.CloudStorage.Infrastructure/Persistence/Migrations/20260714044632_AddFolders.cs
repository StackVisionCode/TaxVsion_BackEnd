using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.CloudStorage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFolders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "FolderId",
                table: "Files",
                type: "uniqueidentifier",
                nullable: true
            );

            migrationBuilder.CreateTable(
                name: "Folders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    OwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ParentFolderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    RelativePath = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Folders", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_Files_TenantId_FolderId",
                table: "Files",
                columns: new[] { "TenantId", "FolderId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_Folders_TenantId_ParentFolderId_Name",
                table: "Folders",
                columns: new[] { "TenantId", "ParentFolderId", "Name" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_Folders_TenantId_RelativePath",
                table: "Folders",
                columns: new[] { "TenantId", "RelativePath" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "Folders");

            migrationBuilder.DropIndex(name: "IX_Files_TenantId_FolderId", table: "Files");

            migrationBuilder.DropColumn(name: "FolderId", table: "Files");
        }
    }
}
