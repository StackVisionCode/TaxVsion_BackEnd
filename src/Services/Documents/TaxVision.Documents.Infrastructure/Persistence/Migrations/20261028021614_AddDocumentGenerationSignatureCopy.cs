using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Documents.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentGenerationSignatureCopy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SignatureAnchorPage",
                schema: "documents",
                table: "DocumentGenerations",
                type: "int",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "SignatureCopyFileId",
                schema: "documents",
                table: "DocumentGenerations",
                type: "uniqueidentifier",
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "SignatureAnchorPage", schema: "documents", table: "DocumentGenerations");

            migrationBuilder.DropColumn(name: "SignatureCopyFileId", schema: "documents", table: "DocumentGenerations");
        }
    }
}
