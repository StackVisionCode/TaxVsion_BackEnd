using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Signature.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BackfillReadyToDraftSignatureRequests : Migration
    {
        // F2: la disponibilidad del documento ya no es un estado. Las filas "Ready" que no se
        // enviaron (SentAtUtc IS NULL, por invariante del aggregate) vuelven a "Draft". El valor
        // del enum se preserva por si hay filas que no satisfacen la invariante en producción.
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE [SignatureRequests] SET [Status] = 'Draft' WHERE [Status] = 'Ready' AND [SentAtUtc] IS NULL;"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reversa best-effort: no se puede distinguir cuáles de los Draft existían previamente
            // en Ready, así que no revertimos. Backfill idempotente: re-aplicar es seguro.
        }
    }
}
