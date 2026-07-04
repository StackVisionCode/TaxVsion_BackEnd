using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.CloudStorage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCloudStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Files",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    OwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FolderType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    TaxYear = table.Column<int>(type: "int", nullable: true),
                    ObjectKey = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                    OriginalName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    DeclaredContentType = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    DetectedContentType = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    ChecksumSha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ScanReport = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ScannedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SoftDeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SoftDeleteExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsLegalHeld = table.Column<bool>(type: "bit", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Files", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "StorageAccessLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    IpAddress = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Details = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorageAccessLogs", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "TenantStorageLimits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    MaxBytes = table.Column<long>(type: "bigint", nullable: false),
                    UsedBytes = table.Column<long>(type: "bigint", nullable: false),
                    ReservedBytes = table.Column<long>(type: "bigint", nullable: false),
                    MaxFileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    IsSuspended = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantStorageLimits", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_Files_TenantId_Id",
                table: "Files",
                columns: new[] { "TenantId", "Id" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_Files_TenantId_ObjectKey",
                table: "Files",
                columns: new[] { "TenantId", "ObjectKey" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_Files_TenantId_Status_CreatedAtUtc",
                table: "Files",
                columns: new[] { "TenantId", "Status", "CreatedAtUtc" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_StorageAccessLogs_TenantId_ActorId_OccurredAtUtc",
                table: "StorageAccessLogs",
                columns: new[] { "TenantId", "ActorId", "OccurredAtUtc" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_StorageAccessLogs_TenantId_FileId_OccurredAtUtc",
                table: "StorageAccessLogs",
                columns: new[] { "TenantId", "FileId", "OccurredAtUtc" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_StorageAccessLogs_TenantId_OccurredAtUtc",
                table: "StorageAccessLogs",
                columns: new[] { "TenantId", "OccurredAtUtc" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_TenantStorageLimits_TenantId",
                table: "TenantStorageLimits",
                column: "TenantId",
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "Files");

            migrationBuilder.DropTable(name: "StorageAccessLogs");

            migrationBuilder.DropTable(name: "TenantStorageLimits");
        }
    }
}
