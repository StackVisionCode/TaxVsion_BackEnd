using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Tasks.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Índice del tablero por columnas: totales por etapa y estado, y tarjetas por vencimiento, solo de tareas
    /// principales. Incluye las columnas de la tarjeta y de la visibilidad para no ir a la tabla. Toca columnas de
    /// tipos owned (DueAtUtc, CustomerId, Title), así que va por SQL directo y no aparece en el ModelSnapshot.
    /// </summary>
    public partial class AddTaskBoardColumnsIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE INDEX IX_Tasks_Board
                    ON Tasks (TenantId, Status, WorkflowStatusCode, DueAtUtc)
                    INCLUDE (Title, Priority, AssigneeUserId, CreatedByUserId, CustomerId, TaxYear, DueTimeZoneId,
                             DueIsStatutory, OpenBlockerCount, OpenSubtaskCount, SubtaskCount, CompletedAtUtc, SeriesId,
                             Estimated, ExpectedItems)
                    WHERE ParentTaskId IS NULL;
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IX_Tasks_Board ON Tasks;");
        }
    }
}
