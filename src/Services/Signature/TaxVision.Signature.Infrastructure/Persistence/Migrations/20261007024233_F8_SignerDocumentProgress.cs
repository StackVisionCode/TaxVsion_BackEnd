using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Signature.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class F8_SignerDocumentProgress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SignerDocumentCompletions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SignerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PartialCopyRequestedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PartialCopySentAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PartialCopyFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PartialCopyFailureReason = table.Column<string>(
                        type: "nvarchar(500)",
                        maxLength: 500,
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SignerDocumentCompletions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SignerDocumentCompletions_Signers_SignerId",
                        column: x => x.SignerId,
                        principalTable: "Signers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "SignerDocumentViews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SignerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FirstViewedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SignerDocumentViews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SignerDocumentViews_Signers_SignerId",
                        column: x => x.SignerId,
                        principalTable: "Signers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_SignerDocumentCompletions_DocumentId",
                table: "SignerDocumentCompletions",
                column: "DocumentId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_SignerDocumentCompletions_SignerId_DocumentId",
                table: "SignerDocumentCompletions",
                columns: new[] { "SignerId", "DocumentId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_SignerDocumentViews_DocumentId",
                table: "SignerDocumentViews",
                column: "DocumentId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_SignerDocumentViews_SignerId_DocumentId",
                table: "SignerDocumentViews",
                columns: new[] { "SignerId", "DocumentId" },
                unique: true
            );

            migrationBuilder.Sql(
                """
                INSERT INTO SignerDocumentViews (Id, SignerId, DocumentId, FirstViewedAtUtc)
                SELECT NEWID(), s.Id, d.Id, s.DocumentFirstViewedAtUtc
                FROM Signers s
                INNER JOIN RequestDocuments d
                    ON d.SignatureRequestId = s.SignatureRequestId AND d.[Order] = 1
                WHERE s.DocumentFirstViewedAtUtc IS NOT NULL;

                INSERT INTO SignerDocumentCompletions
                    (Id, SignerId, DocumentId, CompletedAtUtc, PartialCopyRequestedAtUtc,
                     PartialCopySentAtUtc, PartialCopyFileId, PartialCopyFailureReason)
                SELECT NEWID(), s.Id, sf.DocumentId, COALESCE(s.SignedAtUtc, SYSUTCDATETIME()),
                       s.PartialCopyRequestedAtUtc, s.PartialCopySentAtUtc,
                       s.PartialCopyFileId, s.PartialCopyFailureReason
                FROM Signers s
                INNER JOIN (
                    SELECT DISTINCT SignerId, DocumentId
                    FROM SignatureFields
                ) sf ON sf.SignerId = s.Id
                WHERE s.Status = 'Signed';
                """
            );

            migrationBuilder.DropColumn(name: "DocumentFirstViewedAtUtc", table: "Signers");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DocumentFirstViewedAtUtc",
                table: "Signers",
                type: "datetime2",
                nullable: true
            );

            migrationBuilder.Sql(
                """
                UPDATE s
                SET DocumentFirstViewedAtUtc = views.FirstViewedAtUtc
                FROM Signers s
                INNER JOIN (
                    SELECT SignerId, MIN(FirstViewedAtUtc) AS FirstViewedAtUtc
                    FROM SignerDocumentViews
                    GROUP BY SignerId
                ) views ON views.SignerId = s.Id;
                """
            );

            migrationBuilder.DropTable(name: "SignerDocumentCompletions");

            migrationBuilder.DropTable(name: "SignerDocumentViews");
        }
    }
}
