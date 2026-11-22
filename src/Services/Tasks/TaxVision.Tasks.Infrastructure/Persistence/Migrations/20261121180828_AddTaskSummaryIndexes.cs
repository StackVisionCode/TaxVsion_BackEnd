using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Tasks.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Índices del resumen del dashboard (GET /tasks/summary). DueAtUtc, DueIsStatutory y CustomerId viven en owned
    /// types y HasIndex no los cruza: van por SQL directo y no aparecen en el ModelSnapshot. El INCLUDE lleva las
    /// columnas del recorte por asignación.
    /// </summary>
    public partial class AddTaskSummaryIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE INDEX IX_Tasks_TenantId_Status_DueAtUtc
                    ON Tasks (TenantId, Status, DueAtUtc)
                    INCLUDE (AssigneeUserId, CustomerId, ParentTaskId, CreatedByUserId, DueIsStatutory);
                """
            );

            // Completadas hoy: sin este índice el conteo recorre todas las completadas del tenant.
            migrationBuilder.Sql(
                """
                CREATE INDEX IX_Tasks_TenantId_Status_CompletedAtUtc
                    ON Tasks (TenantId, Status, CompletedAtUtc)
                    INCLUDE (AssigneeUserId, CustomerId, ParentTaskId, CreatedByUserId);
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IX_Tasks_TenantId_Status_CompletedAtUtc ON Tasks;");
            migrationBuilder.Sql("DROP INDEX IX_Tasks_TenantId_Status_DueAtUtc ON Tasks;");
        }
    }
}
