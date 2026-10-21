using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.CloudStorage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantGoogleDriveConnections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CloudProviderOAuthStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InitiatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CloudProviderOAuthStates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TenantCloudConnections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    AccountEmail = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    ProtectedRefreshToken = table.Column<string>(type: "nvarchar(max)", maxLength: 4096, nullable: false),
                    ConnectedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantCloudConnections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TenantCloudFileCopies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    RemoteFileId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ChecksumSha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantCloudFileCopies", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CloudProviderOAuthStates_ExpiresAtUtc",
                table: "CloudProviderOAuthStates",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_TenantCloudConnections_TenantId_Provider",
                table: "TenantCloudConnections",
                columns: new[] { "TenantId", "Provider" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenantCloudFileCopies_TenantId_FileId_Provider",
                table: "TenantCloudFileCopies",
                columns: new[] { "TenantId", "FileId", "Provider" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CloudProviderOAuthStates");

            migrationBuilder.DropTable(
                name: "TenantCloudConnections");

            migrationBuilder.DropTable(
                name: "TenantCloudFileCopies");
        }
    }
}
