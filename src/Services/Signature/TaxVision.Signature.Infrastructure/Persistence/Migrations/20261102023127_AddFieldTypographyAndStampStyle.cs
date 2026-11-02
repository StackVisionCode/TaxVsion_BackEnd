using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Signature.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFieldTypographyAndStampStyle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DateStampFormat",
                table: "TenantSignatureSettings",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                // Oficinas de EE. UU.: la fecha se estampa MM/dd/yyyy (antes salía yyyy-MM-dd fijo).
                defaultValue: "MonthDayYear");

            migrationBuilder.AddColumn<bool>(
                name: "ShowSignatureFrame",
                table: "TenantSignatureSettings",
                type: "bit",
                nullable: false,
                // Todas las firmas ya salían con marco: se conserva hasta que la oficina lo apague.
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "FontSizePt",
                table: "TemplateFields",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AutoFontMaxPt",
                table: "SignaturePlatformSettings",
                type: "int",
                nullable: false,
                defaultValue: 12);

            migrationBuilder.AddColumn<int>(
                name: "AutoFontMinPt",
                table: "SignaturePlatformSettings",
                type: "int",
                nullable: false,
                defaultValue: 6);

            migrationBuilder.AddColumn<int>(
                name: "FontSizePt",
                table: "SignatureFields",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DateStampFormat",
                table: "TenantSignatureSettings");

            migrationBuilder.DropColumn(
                name: "ShowSignatureFrame",
                table: "TenantSignatureSettings");

            migrationBuilder.DropColumn(
                name: "FontSizePt",
                table: "TemplateFields");

            migrationBuilder.DropColumn(
                name: "AutoFontMaxPt",
                table: "SignaturePlatformSettings");

            migrationBuilder.DropColumn(
                name: "AutoFontMinPt",
                table: "SignaturePlatformSettings");

            migrationBuilder.DropColumn(
                name: "FontSizePt",
                table: "SignatureFields");
        }
    }
}
