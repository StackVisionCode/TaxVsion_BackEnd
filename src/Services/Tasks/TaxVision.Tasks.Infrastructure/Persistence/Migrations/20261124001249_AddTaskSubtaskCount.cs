using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Tasks.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskSubtaskCount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SubtaskCount",
                table: "Tasks",
                type: "int",
                nullable: false,
                defaultValue: 0
            );

            // Relleno desde las filas reales. Idempotente; EXEC para que compile aunque la columna sea nueva.
            migrationBuilder.Sql(
                @"EXEC(N'UPDATE t SET SubtaskCount = c.Total
                  FROM Tasks t
                  JOIN (SELECT TenantId, ParentTaskId, COUNT(*) AS Total
                        FROM Tasks WHERE ParentTaskId IS NOT NULL
                        GROUP BY TenantId, ParentTaskId) c
                    ON c.ParentTaskId = t.Id AND c.TenantId = t.TenantId')"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "SubtaskCount", table: "Tasks");
        }
    }
}
