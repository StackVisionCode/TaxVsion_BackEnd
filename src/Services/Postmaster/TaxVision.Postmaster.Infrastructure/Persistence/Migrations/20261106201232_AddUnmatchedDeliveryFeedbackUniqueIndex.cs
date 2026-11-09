using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Postmaster.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUnmatchedDeliveryFeedbackUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Q-PMS-4: el mismo aviso repetido es técnico (se purga a los 30 días); queda el primero recibido.
            migrationBuilder.Sql(
                """
                WITH ranked AS (
                    SELECT ROW_NUMBER() OVER (PARTITION BY Source, SourceEventId ORDER BY ReceivedAtUtc, Id) AS rn
                    FROM UnmatchedDeliveryFeedback
                )
                DELETE FROM ranked WHERE rn > 1;
                """
            );

            migrationBuilder.CreateIndex(
                name: "UX_UnmatchedDeliveryFeedback_Source_SourceEventId",
                table: "UnmatchedDeliveryFeedback",
                columns: new[] { "Source", "SourceEventId" },
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_UnmatchedDeliveryFeedback_Source_SourceEventId",
                table: "UnmatchedDeliveryFeedback"
            );
        }
    }
}
