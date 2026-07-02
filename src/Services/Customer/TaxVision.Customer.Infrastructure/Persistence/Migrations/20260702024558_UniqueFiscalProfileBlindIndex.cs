using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Customer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UniqueFiscalProfileBlindIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CustomerRelationFiscalProfiles_TenantId_TaxIdentifierBlindIndex",
                table: "CustomerRelationFiscalProfiles");

            migrationBuilder.DropIndex(
                name: "IX_CustomerFiscalProfiles_TenantId_TaxIdentifierBlindIndex",
                table: "CustomerFiscalProfiles");

            migrationBuilder.CreateIndex(
                name: "UX_CustomerRelationFiscalProfiles_Tenant_BlindIndex",
                table: "CustomerRelationFiscalProfiles",
                columns: new[] { "TenantId", "TaxIdentifierBlindIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_CustomerFiscalProfiles_Tenant_BlindIndex",
                table: "CustomerFiscalProfiles",
                columns: new[] { "TenantId", "TaxIdentifierBlindIndex" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_CustomerRelationFiscalProfiles_Tenant_BlindIndex",
                table: "CustomerRelationFiscalProfiles");

            migrationBuilder.DropIndex(
                name: "UX_CustomerFiscalProfiles_Tenant_BlindIndex",
                table: "CustomerFiscalProfiles");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerRelationFiscalProfiles_TenantId_TaxIdentifierBlindIndex",
                table: "CustomerRelationFiscalProfiles",
                columns: new[] { "TenantId", "TaxIdentifierBlindIndex" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerFiscalProfiles_TenantId_TaxIdentifierBlindIndex",
                table: "CustomerFiscalProfiles",
                columns: new[] { "TenantId", "TaxIdentifierBlindIndex" });
        }
    }
}
