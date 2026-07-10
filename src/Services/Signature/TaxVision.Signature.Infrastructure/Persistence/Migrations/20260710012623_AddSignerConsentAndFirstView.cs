using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Signature.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSignerConsentAndFirstView : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ConsentAcceptedAtUtc",
                table: "Signers",
                type: "datetime2",
                nullable: true
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "FirstViewedAtUtc",
                table: "Signers",
                type: "datetime2",
                nullable: true
            );

            migrationBuilder.AddColumn<bool>(
                name: "HasAcceptedConsent",
                table: "Signers",
                type: "bit",
                nullable: false,
                defaultValue: false
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ConsentAcceptedAtUtc", table: "Signers");

            migrationBuilder.DropColumn(name: "FirstViewedAtUtc", table: "Signers");

            migrationBuilder.DropColumn(name: "HasAcceptedConsent", table: "Signers");
        }
    }
}
