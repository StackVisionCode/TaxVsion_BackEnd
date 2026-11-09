using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Signature.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSignatureTemplateSettingsRowVersionAndOneDefaultProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Con dos defaults en un ámbito no se crea el índice: correr Q-SIG-7 y que el dueño elija cuál queda.
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM dbo.SignatureProfiles WHERE IsDefault = 1
                           GROUP BY TenantId, OwnerUserId HAVING COUNT(*) > 1)
                    THROW 50000, 'SIG-7: more than one default signature in a (TenantId, OwnerUserId) scope. Run Q-SIG-7 and resolve each case before applying this migration.', 1;
                """
            );

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "TenantSignatureSettings",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "SignatureTemplates",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.CreateIndex(
                name: "UX_SignatureProfiles_OneDefault",
                table: "SignatureProfiles",
                columns: new[] { "TenantId", "OwnerUserId" },
                unique: true,
                filter: "[IsDefault] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_SignatureProfiles_OneDefault",
                table: "SignatureProfiles");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "TenantSignatureSettings");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "SignatureTemplates");
        }
    }
}
