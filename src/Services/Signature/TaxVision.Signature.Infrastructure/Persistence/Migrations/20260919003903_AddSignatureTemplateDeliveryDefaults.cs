using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Signature.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSignatureTemplateDeliveryDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AutoRemindersEnabled",
                table: "SignatureTemplates",
                type: "bit",
                nullable: false,
                // Plantillas existentes: recordatorios activos por defecto (igual que el default de dominio
                // y que el comportamiento previo de tenant settings). Nuevas filas usan el valor del factory.
                defaultValue: true
            );

            migrationBuilder.AddColumn<int>(
                name: "ReminderIntervalHours",
                table: "SignatureTemplates",
                type: "int",
                nullable: false,
                // Nunca 0 (spamearía): 48h por defecto para filas existentes.
                defaultValue: 48
            );

            migrationBuilder.AddColumn<bool>(
                name: "SendCertificateToSigners",
                table: "SignatureTemplates",
                type: "bit",
                nullable: false,
                defaultValue: false
            );

            migrationBuilder.AddColumn<bool>(
                name: "SendSignedDocumentToSigners",
                table: "SignatureTemplates",
                type: "bit",
                nullable: false,
                // Plantillas existentes: entregar el documento firmado por defecto (default de dominio).
                defaultValue: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "AutoRemindersEnabled", table: "SignatureTemplates");

            migrationBuilder.DropColumn(name: "ReminderIntervalHours", table: "SignatureTemplates");

            migrationBuilder.DropColumn(name: "SendCertificateToSigners", table: "SignatureTemplates");

            migrationBuilder.DropColumn(name: "SendSignedDocumentToSigners", table: "SignatureTemplates");
        }
    }
}
