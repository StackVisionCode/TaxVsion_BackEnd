using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Signature.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSignatureAnalyticsSealedRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SignatureAnalyticsSealedRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SignatureRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CountedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SignatureAnalyticsSealedRequests", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SignatureAnalyticsSealedRequests_TenantId_SignatureRequestId",
                table: "SignatureAnalyticsSealedRequests",
                columns: new[] { "TenantId", "SignatureRequestId" },
                unique: true);

            // Las solicitudes ya selladas por completo ya sumaron en RequestsSealed: un re-sellado no debe volver a contarlas.
            migrationBuilder.Sql(
                @"INSERT INTO [SignatureAnalyticsSealedRequests] ([Id], [SignatureRequestId], [CountedAtUtc], [TenantId])
SELECT NEWID(), r.[Id], SYSUTCDATETIME(), r.[TenantId]
FROM [SignatureRequests] r
WHERE r.[Status] = 'Completed'
  AND EXISTS (SELECT 1 FROM [RequestDocuments] d WHERE d.[SignatureRequestId] = r.[Id])
  AND NOT EXISTS (SELECT 1 FROM [RequestDocuments] d WHERE d.[SignatureRequestId] = r.[Id] AND d.[SealedFileId] IS NULL);"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SignatureAnalyticsSealedRequests");
        }
    }
}
