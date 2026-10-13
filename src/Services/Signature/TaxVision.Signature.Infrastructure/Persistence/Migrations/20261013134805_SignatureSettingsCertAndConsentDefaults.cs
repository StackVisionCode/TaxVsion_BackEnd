using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Signature.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SignatureSettingsCertAndConsentDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RequiresConsentByDefault",
                table: "TenantSignatureSettings",
                type: "bit",
                nullable: false,
                defaultValue: true
            );

            migrationBuilder.AddColumn<bool>(
                name: "SendCertificateToSignersDefault",
                table: "TenantSignatureSettings",
                type: "bit",
                nullable: false,
                defaultValue: false
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "RequiresConsentByDefault", table: "TenantSignatureSettings");

            migrationBuilder.DropColumn(name: "SendCertificateToSignersDefault", table: "TenantSignatureSettings");
        }
    }
}
