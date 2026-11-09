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
            // No se borran datos: con duplicados se detiene y lo decide el dueño (ver Q-PMS-4 en el plan).
            migrationBuilder.Sql(
                """
                IF EXISTS (
                    SELECT 1 FROM UnmatchedDeliveryFeedback
                    GROUP BY Source, SourceEventId
                    HAVING COUNT(*) > 1
                )
                    THROW 50001, 'UnmatchedDeliveryFeedback has duplicate (Source, SourceEventId) rows: run Q-PMS-4 and resolve them before creating UX_UnmatchedDeliveryFeedback_Source_SourceEventId.', 1;
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
