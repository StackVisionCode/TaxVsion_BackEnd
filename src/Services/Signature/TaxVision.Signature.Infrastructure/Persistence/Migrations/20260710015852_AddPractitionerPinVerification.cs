using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Signature.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPractitionerPinVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPinVerified",
                table: "Signers",
                type: "bit",
                nullable: false,
                defaultValue: false
            );

            migrationBuilder.AddColumn<int>(
                name: "PinFailedAttempts",
                table: "Signers",
                type: "int",
                nullable: false,
                defaultValue: 0
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "PinLockedUntilUtc",
                table: "Signers",
                type: "datetime2",
                nullable: true
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "PinVerifiedAtUtc",
                table: "Signers",
                type: "datetime2",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "PractitionerPinHash",
                table: "SignatureRequests",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "PractitionerPinSetAtUtc",
                table: "SignatureRequests",
                type: "datetime2",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "PractitionerPinSetByUserId",
                table: "SignatureRequests",
                type: "uniqueidentifier",
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "IsPinVerified", table: "Signers");

            migrationBuilder.DropColumn(name: "PinFailedAttempts", table: "Signers");

            migrationBuilder.DropColumn(name: "PinLockedUntilUtc", table: "Signers");

            migrationBuilder.DropColumn(name: "PinVerifiedAtUtc", table: "Signers");

            migrationBuilder.DropColumn(name: "PractitionerPinHash", table: "SignatureRequests");

            migrationBuilder.DropColumn(name: "PractitionerPinSetAtUtc", table: "SignatureRequests");

            migrationBuilder.DropColumn(name: "PractitionerPinSetByUserId", table: "SignatureRequests");
        }
    }
}
