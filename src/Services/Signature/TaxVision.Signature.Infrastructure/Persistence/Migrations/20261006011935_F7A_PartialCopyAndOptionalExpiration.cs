using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Signature.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class F7A_PartialCopyAndOptionalExpiration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Rename histórico: EF no lo detectó solo porque el modelo cambió el property.
            migrationBuilder.RenameColumn(
                name: "SendSignedDocumentToSigners",
                table: "SignatureRequests",
                newName: "SendSealedDocumentToSigners"
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "PartialCopyRequestedAtUtc",
                table: "Signers",
                type: "datetime2",
                nullable: true
            );

            migrationBuilder.AlterColumn<int>(
                name: "TokenExpirationHours",
                table: "SignatureRequests",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int"
            );

            migrationBuilder.AlterColumn<DateTime>(
                name: "ExpiresAtUtc",
                table: "SignatureRequests",
                type: "datetime2",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "datetime2"
            );

            migrationBuilder.AddColumn<bool>(
                name: "ExpirationEnabled",
                table: "SignatureRequests",
                type: "bit",
                nullable: false,
                defaultValue: true
            );

            migrationBuilder.AddColumn<string>(
                name: "PartialCopyAudience_Kind",
                table: "SignatureRequests",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "All"
            );

            migrationBuilder.AddColumn<string>(
                name: "PartialCopyAudience_SpecificSignerIdsCsv",
                table: "SignatureRequests",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: false,
                defaultValue: ""
            );

            migrationBuilder.AddColumn<bool>(
                name: "SendPartialCopyOnEachSignature",
                table: "SignatureRequests",
                type: "bit",
                nullable: false,
                defaultValue: false
            );

            // Backfill de datos: para filas pre-F7, ExpirationEnabled refleja el valor de ExpiresAtUtc.
            migrationBuilder.Sql(
                "UPDATE [SignatureRequests] SET [ExpirationEnabled] = CASE WHEN [ExpiresAtUtc] IS NULL THEN 0 ELSE 1 END;"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "PartialCopyRequestedAtUtc", table: "Signers");

            migrationBuilder.DropColumn(name: "ExpirationEnabled", table: "SignatureRequests");

            migrationBuilder.DropColumn(name: "PartialCopyAudience_Kind", table: "SignatureRequests");

            migrationBuilder.DropColumn(name: "PartialCopyAudience_SpecificSignerIdsCsv", table: "SignatureRequests");

            migrationBuilder.DropColumn(name: "SendPartialCopyOnEachSignature", table: "SignatureRequests");

            migrationBuilder.AlterColumn<int>(
                name: "TokenExpirationHours",
                table: "SignatureRequests",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<DateTime>(
                name: "ExpiresAtUtc",
                table: "SignatureRequests",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldNullable: true
            );

            migrationBuilder.RenameColumn(
                name: "SendSealedDocumentToSigners",
                table: "SignatureRequests",
                newName: "SendSignedDocumentToSigners"
            );
        }
    }
}
