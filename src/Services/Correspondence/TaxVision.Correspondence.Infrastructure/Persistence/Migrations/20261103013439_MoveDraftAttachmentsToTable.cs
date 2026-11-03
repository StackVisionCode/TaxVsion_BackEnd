using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Correspondence.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MoveDraftAttachmentsToTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DraftAttachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DraftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Filename = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DraftAttachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DraftAttachments_Drafts_DraftId",
                        column: x => x.DraftId,
                        principalTable: "Drafts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IncomingEmailAttachments_TenantId_IncomingEmailId",
                table: "IncomingEmailAttachments",
                columns: new[] { "TenantId", "IncomingEmailId" })
                .Annotation("SqlServer:Include", new[] { "Filename" });

            migrationBuilder.CreateIndex(
                name: "IX_DraftAttachments_DraftId_FileId_Unique",
                table: "DraftAttachments",
                columns: new[] { "DraftId", "FileId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DraftAttachments_TenantId_DraftId",
                table: "DraftAttachments",
                columns: new[] { "TenantId", "DraftId" })
                .Annotation("SqlServer:Include", new[] { "Filename" });

            // Relleno desde el JSON (D-7) ANTES de soltar la columna. sp_executesql: la sentencia se
            // compila al ejecutarse, no junto con el CREATE TABLE de arriba. Un FileId repetido en el
            // JSON entra una sola vez (índice único DraftId+FileId).
            migrationBuilder.Sql(
                @"EXEC sp_executesql N'
INSERT INTO [DraftAttachments] ([Id], [DraftId], [TenantId], [FileId], [Filename], [ContentType], [SizeBytes])
SELECT NEWID(), x.[DraftId], x.[TenantId], x.[FileId], LEFT(x.[Filename], 500), LEFT(x.[ContentType], 100), x.[SizeBytes]
FROM (
    SELECT d.[Id] AS [DraftId], d.[TenantId], j.[FileId], j.[Filename], j.[ContentType], ISNULL(j.[SizeBytes], 0) AS [SizeBytes],
           ROW_NUMBER() OVER (PARTITION BY d.[Id], j.[FileId] ORDER BY (SELECT NULL)) AS [Rn]
    FROM [Drafts] d
    CROSS APPLY OPENJSON(d.[AttachmentsJson]) WITH (
        [FileId] uniqueidentifier ''$.FileId'',
        [Filename] nvarchar(max) ''$.Filename'',
        [ContentType] nvarchar(max) ''$.ContentType'',
        [SizeBytes] bigint ''$.SizeBytes''
    ) j
    WHERE ISJSON(d.[AttachmentsJson]) = 1 AND j.[FileId] IS NOT NULL AND j.[Filename] IS NOT NULL AND j.[ContentType] IS NOT NULL
) x
WHERE x.[Rn] = 1;'"
            );

            // La tabla pasa a ser la única fuente: sin doble escritura que pueda desincronizarse.
            migrationBuilder.DropColumn(name: "AttachmentsJson", table: "Drafts");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AttachmentsJson",
                table: "Drafts",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "[]");

            // Vuelta atrás sin perder adjuntos: la tabla se serializa al mismo JSON que leía la versión anterior.
            migrationBuilder.Sql(
                @"EXEC sp_executesql N'
UPDATE d SET [AttachmentsJson] = ISNULL((
    SELECT a.[FileId], a.[Filename], a.[ContentType], a.[SizeBytes]
    FROM [DraftAttachments] a WHERE a.[DraftId] = d.[Id]
    FOR JSON PATH), ''[]'')
FROM [Drafts] d;'"
            );

            migrationBuilder.DropTable(
                name: "DraftAttachments");

            migrationBuilder.DropIndex(
                name: "IX_IncomingEmailAttachments_TenantId_IncomingEmailId",
                table: "IncomingEmailAttachments");
        }
    }
}
