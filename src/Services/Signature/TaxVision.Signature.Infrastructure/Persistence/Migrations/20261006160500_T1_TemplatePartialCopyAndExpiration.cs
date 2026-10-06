using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Signature.Infrastructure.Persistence.Migrations;

/// <summary>
/// T1 — defaults heredables al instanciar desde plantilla: copia parcial por firma +
/// audiencia (All o Specific por slotOrder) + expiración opcional del link. Son defaults
/// que se copian a la Request creada; en la Request la audiencia se mapea a signerIds reales.
/// </summary>
public partial class T1_TemplatePartialCopyAndExpiration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "SendPartialCopyOnEachSignature",
            table: "SignatureTemplates",
            type: "bit",
            nullable: false,
            defaultValue: false
        );

        migrationBuilder.AddColumn<string>(
            name: "PartialCopyAudienceKind",
            table: "SignatureTemplates",
            type: "nvarchar(16)",
            maxLength: 16,
            nullable: false,
            defaultValue: "All"
        );

        migrationBuilder.AddColumn<string>(
            name: "PartialCopyAudienceSlotOrdersCsv",
            table: "SignatureTemplates",
            type: "nvarchar(256)",
            maxLength: 256,
            nullable: false,
            defaultValue: string.Empty
        );

        migrationBuilder.AddColumn<bool>(
            name: "ExpirationEnabled",
            table: "SignatureTemplates",
            type: "bit",
            nullable: false,
            defaultValue: true
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "SendPartialCopyOnEachSignature", table: "SignatureTemplates");
        migrationBuilder.DropColumn(name: "PartialCopyAudienceKind", table: "SignatureTemplates");
        migrationBuilder.DropColumn(name: "PartialCopyAudienceSlotOrdersCsv", table: "SignatureTemplates");
        migrationBuilder.DropColumn(name: "ExpirationEnabled", table: "SignatureTemplates");
    }
}
