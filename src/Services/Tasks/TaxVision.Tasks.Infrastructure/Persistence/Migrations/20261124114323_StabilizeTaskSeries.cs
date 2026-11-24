using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Tasks.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// La serie guarda la fecha que planificó para la última ocurrencia y siembra desde ahí (C4, C6). Se rellena con el
    /// vencimiento de su última ocurrencia: es lo que el método anterior usaba, así que ninguna serie viva cambia de fecha.
    /// Además, una ocurrencia por número en cada serie (C16); se comprobó antes que no hay duplicados.
    /// </summary>
    public partial class StabilizeTaskSeries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastPlannedDueUtc",
                table: "TaskSeries",
                type: "datetime2",
                nullable: true
            );

            // La columna nace en este mismo lote: el UPDATE va por sp_executesql para que compile después.
            migrationBuilder.Sql(
                """
                EXEC sp_executesql N'
                UPDATE s SET LastPlannedDueUtc = (
                    SELECT TOP 1 t.DueAtUtc FROM Tasks t
                    WHERE t.SeriesId = s.Id
                    ORDER BY t.OccurrenceNumber DESC)
                FROM TaskSeries s';
                """
            );

            migrationBuilder.CreateIndex(
                name: "UX_Tasks_SeriesId_OccurrenceNumber",
                table: "Tasks",
                columns: new[] { "SeriesId", "OccurrenceNumber" },
                unique: true,
                filter: "[SeriesId] IS NOT NULL"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "UX_Tasks_SeriesId_OccurrenceNumber", table: "Tasks");

            migrationBuilder.DropColumn(name: "LastPlannedDueUtc", table: "TaskSeries");
        }
    }
}
