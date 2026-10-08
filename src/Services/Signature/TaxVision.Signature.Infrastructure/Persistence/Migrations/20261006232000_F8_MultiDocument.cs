using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Signature.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class F8_MultiDocument : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DocumentId",
                table: "SignatureFields",
                type: "uniqueidentifier",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DocumentId",
                table: "PreparerFields",
                type: "uniqueidentifier",
                nullable: true
            );

            migrationBuilder.CreateTable(
                name: "RequestDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SignatureRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    OriginalFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentHashPre = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    SealedFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DocumentHashPost = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    SealedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequestDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RequestDocuments_SignatureRequests_SignatureRequestId",
                        column: x => x.SignatureRequestId,
                        principalTable: "SignatureRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            // Backfill no destructivo: cada solicitud histórica se convierte en una solicitud
            // de un documento y todos sus campos quedan anclados a ese documento.
            migrationBuilder.Sql(
                """
                INSERT INTO [RequestDocuments]
                    ([Id], [SignatureRequestId], [Order], [Title], [OriginalFileId], [DocumentHashPre],
                     [SealedFileId], [DocumentHashPost], [SealedAtUtc], [Note])
                SELECT NEWID(), [Id], 1, LEFT([Title], 200), [OriginalFileId], [DocumentHashPre],
                       [SealedFileId], [DocumentHashPost],
                       CASE WHEN [SealedFileId] IS NULL THEN NULL ELSE COALESCE([CompletedAtUtc], [UpdatedAtUtc]) END,
                       NULL
                FROM [SignatureRequests];

                UPDATE field
                SET [DocumentId] = document.[Id]
                FROM [SignatureFields] AS field
                INNER JOIN [RequestDocuments] AS document
                    ON document.[SignatureRequestId] = field.[SignatureRequestId]
                   AND document.[Order] = 1;

                UPDATE field
                SET [DocumentId] = document.[Id]
                FROM [PreparerFields] AS field
                INNER JOIN [RequestDocuments] AS document
                    ON document.[SignatureRequestId] = field.[SignatureRequestId]
                   AND document.[Order] = 1;
                """
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DocumentId",
                table: "SignatureFields",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "DocumentId",
                table: "PreparerFields",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_SignatureFields_DocumentId",
                table: "SignatureFields",
                column: "DocumentId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_PreparerFields_DocumentId",
                table: "PreparerFields",
                column: "DocumentId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_RequestDocuments_OriginalFileId",
                table: "RequestDocuments",
                column: "OriginalFileId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_RequestDocuments_SealedFileId",
                table: "RequestDocuments",
                column: "SealedFileId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_RequestDocuments_SignatureRequestId_Order",
                table: "RequestDocuments",
                columns: new[] { "SignatureRequestId", "Order" },
                unique: true
            );

            migrationBuilder.AddForeignKey(
                name: "FK_PreparerFields_RequestDocuments_DocumentId",
                table: "PreparerFields",
                column: "DocumentId",
                principalTable: "RequestDocuments",
                principalColumn: "Id"
            );

            migrationBuilder.AddForeignKey(
                name: "FK_SignatureFields_RequestDocuments_DocumentId",
                table: "SignatureFields",
                column: "DocumentId",
                principalTable: "RequestDocuments",
                principalColumn: "Id"
            );

            migrationBuilder.DropColumn(name: "DocumentHashPost", table: "SignatureRequests");
            migrationBuilder.DropColumn(name: "DocumentHashPre", table: "SignatureRequests");
            migrationBuilder.DropColumn(name: "OriginalFileId", table: "SignatureRequests");
            migrationBuilder.DropColumn(name: "SealedFileId", table: "SignatureRequests");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reversible solo para requests que no agregaron un segundo documento tras aplicar Up.
            migrationBuilder.DropForeignKey(
                name: "FK_PreparerFields_RequestDocuments_DocumentId",
                table: "PreparerFields"
            );

            migrationBuilder.DropForeignKey(
                name: "FK_SignatureFields_RequestDocuments_DocumentId",
                table: "SignatureFields"
            );

            migrationBuilder.DropIndex(name: "IX_SignatureFields_DocumentId", table: "SignatureFields");

            migrationBuilder.DropIndex(name: "IX_PreparerFields_DocumentId", table: "PreparerFields");

            migrationBuilder.AddColumn<string>(
                name: "DocumentHashPost",
                table: "SignatureRequests",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "DocumentHashPre",
                table: "SignatureRequests",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "OriginalFileId",
                table: "SignatureRequests",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000")
            );

            migrationBuilder.AddColumn<Guid>(
                name: "SealedFileId",
                table: "SignatureRequests",
                type: "uniqueidentifier",
                nullable: true
            );

            migrationBuilder.Sql(
                """
                UPDATE request
                SET [OriginalFileId] = document.[OriginalFileId],
                    [DocumentHashPre] = document.[DocumentHashPre],
                    [SealedFileId] = document.[SealedFileId],
                    [DocumentHashPost] = document.[DocumentHashPost]
                FROM [SignatureRequests] AS request
                INNER JOIN [RequestDocuments] AS document
                    ON document.[SignatureRequestId] = request.[Id]
                   AND document.[Order] = 1;
                """
            );

            migrationBuilder.DropColumn(name: "DocumentId", table: "SignatureFields");
            migrationBuilder.DropColumn(name: "DocumentId", table: "PreparerFields");
            migrationBuilder.DropTable(name: "RequestDocuments");
        }
    }
}
