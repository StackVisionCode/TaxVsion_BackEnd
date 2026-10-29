using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Tasks.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFormRevisionSignature : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DocumentHashPost",
                table: "FormSubmissionRevisions",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "DocumentHashPre",
                table: "FormSubmissionRevisions",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "SealedFileId",
                table: "FormSubmissionRevisions",
                type: "uniqueidentifier",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "SignatureCertificateFileId",
                table: "FormSubmissionRevisions",
                type: "uniqueidentifier",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "SignatureCopyFileId",
                table: "FormSubmissionRevisions",
                type: "uniqueidentifier",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "SignatureRequestId",
                table: "FormSubmissionRevisions",
                type: "uniqueidentifier",
                nullable: true
            );

            migrationBuilder.AddColumn<int>(
                name: "SignatureStatus",
                table: "FormSubmissionRevisions",
                type: "int",
                nullable: false,
                defaultValue: 0
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "DocumentHashPost", table: "FormSubmissionRevisions");

            migrationBuilder.DropColumn(name: "DocumentHashPre", table: "FormSubmissionRevisions");

            migrationBuilder.DropColumn(name: "SealedFileId", table: "FormSubmissionRevisions");

            migrationBuilder.DropColumn(name: "SignatureCertificateFileId", table: "FormSubmissionRevisions");

            migrationBuilder.DropColumn(name: "SignatureCopyFileId", table: "FormSubmissionRevisions");

            migrationBuilder.DropColumn(name: "SignatureRequestId", table: "FormSubmissionRevisions");

            migrationBuilder.DropColumn(name: "SignatureStatus", table: "FormSubmissionRevisions");
        }
    }
}
