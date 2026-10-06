using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Signature.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class F7C_TenantSettingsDeliveryDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ExpirationEnabledByDefault",
                table: "TenantSignatureSettings",
                type: "bit",
                nullable: false,
                defaultValue: true
            );

            migrationBuilder.AddColumn<string>(
                name: "PartialCopyDefaultAudienceKind",
                table: "TenantSignatureSettings",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "All"
            );

            migrationBuilder.AddColumn<bool>(
                name: "SendPartialCopyDefault",
                table: "TenantSignatureSettings",
                type: "bit",
                nullable: false,
                defaultValue: false
            );

            migrationBuilder.AddColumn<bool>(
                name: "SendSealedDocumentDefault",
                table: "TenantSignatureSettings",
                type: "bit",
                nullable: false,
                defaultValue: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ExpirationEnabledByDefault", table: "TenantSignatureSettings");

            migrationBuilder.DropColumn(name: "PartialCopyDefaultAudienceKind", table: "TenantSignatureSettings");

            migrationBuilder.DropColumn(name: "SendPartialCopyDefault", table: "TenantSignatureSettings");

            migrationBuilder.DropColumn(name: "SendSealedDocumentDefault", table: "TenantSignatureSettings");
        }
    }
}
