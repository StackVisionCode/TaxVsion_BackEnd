using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Signature.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCertificateGenerationMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CertificateGenerationMode",
                table: "SignatureRequests",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "SingleForRequest");

            migrationBuilder.AddColumn<Guid>(
                name: "CertificateFileId",
                table: "RequestDocuments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RequestDocuments_CertificateFileId",
                table: "RequestDocuments",
                column: "CertificateFileId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RequestDocuments_CertificateFileId",
                table: "RequestDocuments");

            migrationBuilder.DropColumn(
                name: "CertificateGenerationMode",
                table: "SignatureRequests");

            migrationBuilder.DropColumn(
                name: "CertificateFileId",
                table: "RequestDocuments");
        }
    }
}
