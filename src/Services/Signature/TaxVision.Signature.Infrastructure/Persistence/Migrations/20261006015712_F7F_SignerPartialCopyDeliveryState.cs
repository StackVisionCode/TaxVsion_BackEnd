using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Signature.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class F7F_SignerPartialCopyDeliveryState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PartialCopyFailureReason",
                table: "Signers",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "PartialCopyFileId",
                table: "Signers",
                type: "uniqueidentifier",
                nullable: true
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "PartialCopySentAtUtc",
                table: "Signers",
                type: "datetime2",
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "PartialCopyFailureReason", table: "Signers");

            migrationBuilder.DropColumn(name: "PartialCopyFileId", table: "Signers");

            migrationBuilder.DropColumn(name: "PartialCopySentAtUtc", table: "Signers");
        }
    }
}
