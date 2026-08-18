using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Documents.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(name: "documents");

            migrationBuilder.CreateTable(
                name: "DocumentGenerations",
                schema: "documents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentType = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    TemplateKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TemplateVersion = table.Column<int>(type: "int", nullable: false),
                    OutputFormat = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OwnerType = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    OwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceService = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    DocumentVersion = table.Column<int>(type: "int", nullable: false),
                    Priority = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    FileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StorageFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StorageContentType = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    StorageSizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    StorageChecksumSha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    FileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    ContentHash = table.Column<string>(type: "char(64)", fixedLength: true, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    ErrorCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CausationId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    RequestedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentGenerations", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_DocumentGenerations_FileId",
                schema: "documents",
                table: "DocumentGenerations",
                column: "FileId",
                filter: "[FileId] IS NOT NULL"
            );

            migrationBuilder.CreateIndex(
                name: "IX_DocumentGenerations_Tenant_Status",
                schema: "documents",
                table: "DocumentGenerations",
                columns: new[] { "TenantId", "Status" }
            );

            migrationBuilder.CreateIndex(
                name: "UX_DocumentGenerations_Tenant_Source_IdempotencyKey",
                schema: "documents",
                table: "DocumentGenerations",
                columns: new[] { "TenantId", "SourceService", "IdempotencyKey" },
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "DocumentGenerations", schema: "documents");
        }
    }
}
