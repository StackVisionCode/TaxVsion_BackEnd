using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Signature.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSignatureRequestDeliveryFlags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "SendCertificateToSigners",
                table: "SignatureRequests",
                type: "bit",
                nullable: false,
                defaultValue: false
            );

            // Default TRUE: preserva el comportamiento histórico (el documento firmado siempre se
            // emailaba). Las filas existentes backfillean a true; el default C# del aggregate también es true.
            migrationBuilder.AddColumn<bool>(
                name: "SendSignedDocumentToSigners",
                table: "SignatureRequests",
                type: "bit",
                nullable: false,
                defaultValue: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "SendCertificateToSigners", table: "SignatureRequests");

            migrationBuilder.DropColumn(name: "SendSignedDocumentToSigners", table: "SignatureRequests");
        }
    }
}
