using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Signature.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCaptureMethodEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CaptureMethod",
                table: "Signers",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "SignatureImageFileId",
                table: "Signers",
                type: "uniqueidentifier",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "TypedName",
                table: "Signers",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "CaptureMethod", table: "Signers");

            migrationBuilder.DropColumn(name: "SignatureImageFileId", table: "Signers");

            migrationBuilder.DropColumn(name: "TypedName", table: "Signers");
        }
    }
}
