using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Signature.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class F8_TemplateMultiDocument : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TemplatePreparerFields_SignatureTemplateId",
                table: "TemplatePreparerFields"
            );

            migrationBuilder.DropIndex(
                name: "IX_TemplateFields_SignatureTemplateId_SlotOrder",
                table: "TemplateFields"
            );

            migrationBuilder.AddColumn<Guid>(
                name: "TemplateDocumentId",
                table: "TemplatePreparerFields",
                type: "uniqueidentifier",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "TemplateDocumentId",
                table: "TemplateFields",
                type: "uniqueidentifier",
                nullable: true
            );

            migrationBuilder.CreateTable(
                name: "TemplateDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SignatureTemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    FileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TemplateDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TemplateDocuments_SignatureTemplates_SignatureTemplateId",
                        column: x => x.SignatureTemplateId,
                        principalTable: "SignatureTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "SignatureTemplateMigrationQuarantines",
                columns: table => new
                {
                    TemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OriginalStatus = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    OriginalPublishedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    OriginalArchivedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    OriginalUpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    QuarantinedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SignatureTemplateMigrationQuarantines", x => x.TemplateId);
                    table.ForeignKey(
                        name: "FK_SignatureTemplateMigrationQuarantines_SignatureTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "SignatureTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.Sql(
                """
                DECLARE @quarantinedAtUtc datetime2 = SYSUTCDATETIME();

                INSERT INTO SignatureTemplateMigrationQuarantines (
                    TemplateId,
                    OriginalStatus,
                    OriginalPublishedAtUtc,
                    OriginalArchivedAtUtc,
                    OriginalUpdatedAtUtc,
                    QuarantinedAtUtc,
                    Reason
                )
                SELECT
                    t.Id,
                    t.Status,
                    t.PublishedAtUtc,
                    t.ArchivedAtUtc,
                    t.UpdatedAtUtc,
                    @quarantinedAtUtc,
                    N'Legacy template has fields but no base document.'
                FROM SignatureTemplates t
                WHERE t.BaseDocumentFileId IS NULL
                  AND (
                      EXISTS (SELECT 1 FROM TemplateFields f WHERE f.SignatureTemplateId = t.Id)
                      OR EXISTS (SELECT 1 FROM TemplatePreparerFields p WHERE p.SignatureTemplateId = t.Id)
                  );

                -- Keep legacy data repairable but fail closed: Draft templates cannot be instantiated.
                UPDATE t
                SET t.Status = N'Draft',
                    t.PublishedAtUtc = NULL,
                    t.ArchivedAtUtc = NULL,
                    t.UpdatedAtUtc = @quarantinedAtUtc
                FROM SignatureTemplates t
                INNER JOIN SignatureTemplateMigrationQuarantines q ON q.TemplateId = t.Id;

                INSERT INTO TemplateDocuments (Id, SignatureTemplateId, [Order], FileId, Title)
                SELECT
                    NEWID(),
                    t.Id,
                    1,
                    COALESCE(t.BaseDocumentFileId, CAST('00000000-0000-0000-0000-000000000000' AS uniqueidentifier)),
                    t.Title
                FROM SignatureTemplates t
                WHERE t.BaseDocumentFileId IS NOT NULL
                   OR EXISTS (
                       SELECT 1
                       FROM SignatureTemplateMigrationQuarantines q
                       WHERE q.TemplateId = t.Id
                   );

                UPDATE f
                SET f.TemplateDocumentId = d.Id
                FROM TemplateFields f
                INNER JOIN TemplateDocuments d ON d.SignatureTemplateId = f.SignatureTemplateId AND d.[Order] = 1;

                UPDATE p
                SET p.TemplateDocumentId = d.Id
                FROM TemplatePreparerFields p
                INNER JOIN TemplateDocuments d ON d.SignatureTemplateId = p.SignatureTemplateId AND d.[Order] = 1;

                IF EXISTS (SELECT 1 FROM TemplateFields WHERE TemplateDocumentId IS NULL)
                   OR EXISTS (SELECT 1 FROM TemplatePreparerFields WHERE TemplateDocumentId IS NULL)
                    THROW 51000, 'Template document backfill did not cover every legacy field.', 1;
                """
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "TemplateDocumentId",
                table: "TemplatePreparerFields",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "TemplateDocumentId",
                table: "TemplateFields",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_TemplatePreparerFields_SignatureTemplateId_TemplateDocumentId",
                table: "TemplatePreparerFields",
                columns: new[] { "SignatureTemplateId", "TemplateDocumentId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_TemplatePreparerFields_TemplateDocumentId",
                table: "TemplatePreparerFields",
                column: "TemplateDocumentId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_TemplateFields_SignatureTemplateId_TemplateDocumentId_SlotOrder",
                table: "TemplateFields",
                columns: new[] { "SignatureTemplateId", "TemplateDocumentId", "SlotOrder" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_TemplateFields_TemplateDocumentId",
                table: "TemplateFields",
                column: "TemplateDocumentId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_TemplateDocuments_SignatureTemplateId_FileId",
                table: "TemplateDocuments",
                columns: new[] { "SignatureTemplateId", "FileId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_TemplateDocuments_SignatureTemplateId_Order",
                table: "TemplateDocuments",
                columns: new[] { "SignatureTemplateId", "Order" },
                unique: true
            );

            migrationBuilder.AddForeignKey(
                name: "FK_TemplateFields_TemplateDocuments_TemplateDocumentId",
                table: "TemplateFields",
                column: "TemplateDocumentId",
                principalTable: "TemplateDocuments",
                principalColumn: "Id"
            );

            migrationBuilder.AddForeignKey(
                name: "FK_TemplatePreparerFields_TemplateDocuments_TemplateDocumentId",
                table: "TemplatePreparerFields",
                column: "TemplateDocumentId",
                principalTable: "TemplateDocuments",
                principalColumn: "Id"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE t
                SET t.Status = q.OriginalStatus,
                    t.PublishedAtUtc = q.OriginalPublishedAtUtc,
                    t.ArchivedAtUtc = q.OriginalArchivedAtUtc,
                    t.UpdatedAtUtc = q.OriginalUpdatedAtUtc
                FROM SignatureTemplates t
                INNER JOIN SignatureTemplateMigrationQuarantines q ON q.TemplateId = t.Id;
                """
            );

            migrationBuilder.DropForeignKey(
                name: "FK_TemplateFields_TemplateDocuments_TemplateDocumentId",
                table: "TemplateFields"
            );

            migrationBuilder.DropForeignKey(
                name: "FK_TemplatePreparerFields_TemplateDocuments_TemplateDocumentId",
                table: "TemplatePreparerFields"
            );

            migrationBuilder.DropTable(name: "SignatureTemplateMigrationQuarantines");

            migrationBuilder.DropTable(name: "TemplateDocuments");

            migrationBuilder.DropIndex(
                name: "IX_TemplatePreparerFields_SignatureTemplateId_TemplateDocumentId",
                table: "TemplatePreparerFields"
            );

            migrationBuilder.DropIndex(
                name: "IX_TemplatePreparerFields_TemplateDocumentId",
                table: "TemplatePreparerFields"
            );

            migrationBuilder.DropIndex(
                name: "IX_TemplateFields_SignatureTemplateId_TemplateDocumentId_SlotOrder",
                table: "TemplateFields"
            );

            migrationBuilder.DropIndex(name: "IX_TemplateFields_TemplateDocumentId", table: "TemplateFields");

            migrationBuilder.DropColumn(name: "TemplateDocumentId", table: "TemplatePreparerFields");

            migrationBuilder.DropColumn(name: "TemplateDocumentId", table: "TemplateFields");

            migrationBuilder.CreateIndex(
                name: "IX_TemplatePreparerFields_SignatureTemplateId",
                table: "TemplatePreparerFields",
                column: "SignatureTemplateId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_TemplateFields_SignatureTemplateId_SlotOrder",
                table: "TemplateFields",
                columns: new[] { "SignatureTemplateId", "SlotOrder" }
            );
        }
    }
}
