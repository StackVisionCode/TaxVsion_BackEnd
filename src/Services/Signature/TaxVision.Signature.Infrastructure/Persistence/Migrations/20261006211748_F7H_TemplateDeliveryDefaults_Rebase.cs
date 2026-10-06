using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Signature.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class F7H_TemplateDeliveryDefaults_Rebase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // F7H — el entity renombró SendSignedDocumentToSigners → SendSealedDocumentToSigners
            // pero la migración F7A solo renombró en SignatureRequests, no en SignatureTemplates.
            // Esta migración cierra ese drift en prod (en local ya se renombró manualmente).
            // Se hace condicional para que una base ya renombrada no falle: EF Core traduce esto a SQL puro.
            migrationBuilder.Sql(
                "IF EXISTS (SELECT 1 FROM sys.columns WHERE Name = N'SendSignedDocumentToSigners' AND Object_ID = OBJECT_ID(N'SignatureTemplates')) "
                    + "AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE Name = N'SendSealedDocumentToSigners' AND Object_ID = OBJECT_ID(N'SignatureTemplates')) "
                    + "EXEC sp_rename N'SignatureTemplates.SendSignedDocumentToSigners', N'SendSealedDocumentToSigners', N'COLUMN';"
            );

            migrationBuilder.AddColumn<bool>(
                name: "ExpirationEnabled",
                table: "SignatureTemplates",
                type: "bit",
                nullable: false,
                defaultValue: true
            );

            migrationBuilder.AddColumn<string>(
                name: "PartialCopyAudienceKind",
                table: "SignatureTemplates",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: ""
            );

            migrationBuilder.AddColumn<string>(
                name: "PartialCopyAudienceSlotOrdersCsv",
                table: "SignatureTemplates",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: ""
            );

            migrationBuilder.AddColumn<bool>(
                name: "SendPartialCopyOnEachSignature",
                table: "SignatureTemplates",
                type: "bit",
                nullable: false,
                defaultValue: false
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ExpirationEnabled", table: "SignatureTemplates");

            migrationBuilder.DropColumn(name: "PartialCopyAudienceKind", table: "SignatureTemplates");

            migrationBuilder.DropColumn(name: "PartialCopyAudienceSlotOrdersCsv", table: "SignatureTemplates");

            migrationBuilder.DropColumn(name: "SendPartialCopyOnEachSignature", table: "SignatureTemplates");

            // F7H — reversa condicional del rename.
            migrationBuilder.Sql(
                "IF EXISTS (SELECT 1 FROM sys.columns WHERE Name = N'SendSealedDocumentToSigners' AND Object_ID = OBJECT_ID(N'SignatureTemplates')) "
                    + "AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE Name = N'SendSignedDocumentToSigners' AND Object_ID = OBJECT_ID(N'SignatureTemplates')) "
                    + "EXEC sp_rename N'SignatureTemplates.SendSealedDocumentToSigners', N'SendSignedDocumentToSigners', N'COLUMN';"
            );
        }
    }
}
