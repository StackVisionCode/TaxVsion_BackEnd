using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.CloudStorage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCloudMirrorFolders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RemotePath",
                table: "TenantCloudFileCopies",
                type: "nvarchar(1024)",
                maxLength: 1024,
                nullable: true
            );

            migrationBuilder.CreateTable(
                name: "TenantCloudFolders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Path = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    RemoteFolderId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantCloudFolders", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_TenantCloudFolders_TenantId_Provider_Path",
                table: "TenantCloudFolders",
                columns: new[] { "TenantId", "Provider", "Path" },
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "TenantCloudFolders");

            migrationBuilder.DropColumn(name: "RemotePath", table: "TenantCloudFileCopies");
        }
    }
}
