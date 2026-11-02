using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Signature.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRequestDocumentVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RejectionCode",
                table: "RequestDocuments",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                table: "RequestDocuments",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerificationStatus",
                table: "RequestDocuments",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Pending");

            // Los documentos que ya tenían hash se adjuntaron antes de existir la verificación del servidor:
            // quedan como verificados para no bloquear borradores ni solicitudes en curso. sp_executesql para
            // que la columna recién creada no rompa la compilación del lote (scripts idempotentes).
            migrationBuilder.Sql(
                "EXEC sp_executesql N'UPDATE [RequestDocuments] SET [VerificationStatus] = N''Verified'' "
                    + "WHERE [DocumentHashPre] IS NOT NULL';"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RejectionCode",
                table: "RequestDocuments");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                table: "RequestDocuments");

            migrationBuilder.DropColumn(
                name: "VerificationStatus",
                table: "RequestDocuments");
        }
    }
}
