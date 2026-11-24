using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Tasks.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Las plantillas estándar se reconocen por clave (R1.8). Las ya instaladas reciben la suya por su nombre, antiguo o
    /// actual; si una oficina tuviera dos copias de la misma, la clave es de la más antigua. Se comprobó antes que no hay.
    /// </summary>
    public partial class AddStandardTemplateKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StandardKey",
                table: "TaskTemplates",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true
            );

            migrationBuilder.Sql(
                """
                EXEC sp_executesql N'
                WITH Matches AS (
                    SELECT Id, CASE
                        WHEN Name IN (N''1040 — Individual Return'', N''1040 — Declaración individual'') THEN ''1040''
                        WHEN Name IN (N''1040-ES — Quarterly Estimated Payments'', N''1040-ES — Pagos estimados trimestrales'') THEN ''1040-es''
                        WHEN Name IN (N''941 — Quarterly Payroll Return'', N''941 — Declaración trimestral de nómina'') THEN ''941''
                    END AS StandardKey, TenantId, CreatedAtUtc
                    FROM TaskTemplates),
                Ranked AS (
                    SELECT Id, StandardKey, ROW_NUMBER() OVER (PARTITION BY TenantId, StandardKey ORDER BY CreatedAtUtc) AS Position
                    FROM Matches WHERE StandardKey IS NOT NULL)
                UPDATE t SET StandardKey = r.StandardKey
                FROM TaskTemplates t JOIN Ranked r ON r.Id = t.Id
                WHERE r.Position = 1';
                """
            );

            migrationBuilder.CreateIndex(
                name: "UX_TaskTemplates_TenantId_StandardKey",
                table: "TaskTemplates",
                columns: new[] { "TenantId", "StandardKey" },
                unique: true,
                filter: "[StandardKey] IS NOT NULL"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "UX_TaskTemplates_TenantId_StandardKey", table: "TaskTemplates");

            migrationBuilder.DropColumn(name: "StandardKey", table: "TaskTemplates");
        }
    }
}
