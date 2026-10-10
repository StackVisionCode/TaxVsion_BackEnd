using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Signature.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Repara el drift del snapshot: añade a `SignatureRequests` la columna
    /// `CertificateGenerationMode` (modo per-request / per-document) y a
    /// `RequestDocuments` la columna `CertificateFileId` + su índice. El modelo las
    /// había introducido en Fase F8-F9 (certificados per-doc), pero nunca se generó
    /// una migración, así que prod las ve como "Invalid column name" en todo
    /// GET /signature/requests/{id}. Las sentencias son idempotentes para que dev,
    /// cuya BD ya tiene las columnas por re-creación, no falle.
    /// </summary>
    public partial class F9B_SignatureRequestCertificateFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF COL_LENGTH('dbo.SignatureRequests', 'CertificateGenerationMode') IS NULL
                BEGIN
                    ALTER TABLE [dbo].[SignatureRequests]
                        ADD [CertificateGenerationMode] NVARCHAR(32) NOT NULL
                        CONSTRAINT [DF_SignatureRequests_CertificateGenerationMode]
                        DEFAULT (N'SingleForRequest');
                END;
                """
            );

            migrationBuilder.Sql(
                """
                IF COL_LENGTH('dbo.RequestDocuments', 'CertificateFileId') IS NULL
                BEGIN
                    ALTER TABLE [dbo].[RequestDocuments]
                        ADD [CertificateFileId] UNIQUEIDENTIFIER NULL;
                END;
                """
            );

            migrationBuilder.Sql(
                """
                IF NOT EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE [name] = 'IX_RequestDocuments_CertificateFileId'
                      AND [object_id] = OBJECT_ID('dbo.RequestDocuments')
                )
                BEGIN
                    CREATE INDEX [IX_RequestDocuments_CertificateFileId]
                        ON [dbo].[RequestDocuments]([CertificateFileId]);
                END;
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE [name] = 'IX_RequestDocuments_CertificateFileId'
                      AND [object_id] = OBJECT_ID('dbo.RequestDocuments')
                )
                BEGIN
                    DROP INDEX [IX_RequestDocuments_CertificateFileId]
                        ON [dbo].[RequestDocuments];
                END;
                """
            );

            migrationBuilder.Sql(
                """
                IF COL_LENGTH('dbo.RequestDocuments', 'CertificateFileId') IS NOT NULL
                BEGIN
                    ALTER TABLE [dbo].[RequestDocuments] DROP COLUMN [CertificateFileId];
                END;
                """
            );

            migrationBuilder.Sql(
                """
                IF COL_LENGTH('dbo.SignatureRequests', 'CertificateGenerationMode') IS NOT NULL
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM sys.default_constraints
                        WHERE [name] = 'DF_SignatureRequests_CertificateGenerationMode'
                    )
                    BEGIN
                        ALTER TABLE [dbo].[SignatureRequests]
                            DROP CONSTRAINT [DF_SignatureRequests_CertificateGenerationMode];
                    END;
                    ALTER TABLE [dbo].[SignatureRequests] DROP COLUMN [CertificateGenerationMode];
                END;
                """
            );
        }
    }
}
