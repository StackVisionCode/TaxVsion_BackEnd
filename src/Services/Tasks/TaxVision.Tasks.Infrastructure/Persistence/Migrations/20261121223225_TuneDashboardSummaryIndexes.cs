using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Tasks.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Índices del resumen y de la búsqueda por vencimiento medidos con volumen. T-1 en la proyección de asignaciones.
    /// Los de Tasks tocan columnas de owned types y van por SQL directo, como AddTaskSummaryIndexes: el de vencimiento
    /// suma Title y DueTimeZoneId (top y vencimientos legales sin lookups) y el de creación ordena la búsqueda.
    /// </summary>
    public partial class TuneDashboardSummaryIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder
                .CreateIndex(
                    name: "IX_CustomerAssignmentProjections_TenantId_UserId",
                    table: "CustomerAssignmentProjections",
                    columns: new[] { "TenantId", "UserId" }
                )
                .Annotation("SqlServer:Include", new[] { "CustomerId" });

            migrationBuilder.Sql(
                """
                CREATE INDEX IX_Tasks_TenantId_Status_DueAtUtc
                    ON Tasks (TenantId, Status, DueAtUtc)
                    INCLUDE (AssigneeUserId, CustomerId, ParentTaskId, CreatedByUserId, DueIsStatutory, Title, DueTimeZoneId)
                    WITH (DROP_EXISTING = ON);
                """
            );

            migrationBuilder.Sql(
                """
                CREATE INDEX IX_Tasks_TenantId_CreatedAtUtc
                    ON Tasks (TenantId, CreatedAtUtc DESC, Id);
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IX_Tasks_TenantId_CreatedAtUtc ON Tasks;");

            migrationBuilder.Sql(
                """
                CREATE INDEX IX_Tasks_TenantId_Status_DueAtUtc
                    ON Tasks (TenantId, Status, DueAtUtc)
                    INCLUDE (AssigneeUserId, CustomerId, ParentTaskId, CreatedByUserId, DueIsStatutory)
                    WITH (DROP_EXISTING = ON);
                """
            );

            migrationBuilder.DropIndex(
                name: "IX_CustomerAssignmentProjections_TenantId_UserId",
                table: "CustomerAssignmentProjections"
            );
        }
    }
}
