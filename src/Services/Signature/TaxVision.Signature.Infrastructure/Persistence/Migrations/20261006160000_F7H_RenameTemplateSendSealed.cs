using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Signature.Infrastructure.Persistence.Migrations;

/// <summary>
/// F7.A olvidó renombrar la columna en SignatureTemplates. El entity ya se llama
/// SendSealedDocumentToSigners (consistente con SignatureRequests), así que la DB tiene que
/// seguir — hasta ahora GetTemplateById reventaba con "Invalid column name".
/// </summary>
public partial class F7H_RenameTemplateSendSealed : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.RenameColumn(
            name: "SendSignedDocumentToSigners",
            table: "SignatureTemplates",
            newName: "SendSealedDocumentToSigners"
        );

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.RenameColumn(
            name: "SendSealedDocumentToSigners",
            table: "SignatureTemplates",
            newName: "SendSignedDocumentToSigners"
        );
}
