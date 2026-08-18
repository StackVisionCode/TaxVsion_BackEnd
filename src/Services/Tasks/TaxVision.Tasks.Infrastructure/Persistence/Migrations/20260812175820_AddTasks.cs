using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Tasks.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Tasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssigneeUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TaxYear = table.Column<int>(type: "int", nullable: true),
                    DueAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DueTimeZoneId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    DueIsStatutory = table.Column<bool>(type: "bit", nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ParentTaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Depth = table.Column<int>(type: "int", nullable: false),
                    OpenSubtaskCount = table.Column<int>(type: "int", nullable: false),
                    OpenBlockerCount = table.Column<int>(type: "int", nullable: false),
                    SeriesId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OccurrenceNumber = table.Column<int>(type: "int", nullable: true),
                    Estimated = table.Column<decimal>(type: "decimal(6,2)", nullable: true),
                    ExpectedItems = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ClientDueAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClientRequestedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClientRequestedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tasks", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_TenantId_ParentTaskId",
                table: "Tasks",
                columns: new[] { "TenantId", "ParentTaskId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_TenantId_SeriesId",
                table: "Tasks",
                columns: new[] { "TenantId", "SeriesId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_TenantId_Status_ClientDueAtUtc_WaitingOnClient",
                table: "Tasks",
                columns: new[] { "TenantId", "Status", "ClientDueAtUtc" },
                filter: "[Status] = 3"
            );

            // Los tres de abajo van con SQL directo y no es una preferencia de estilo: sus columnas
            // viven dentro de un owned type (Due, Reference) y HasIndex no cruza entity types aunque
            // compartan tabla — falla en design-time con "is not a valid member access expression".
            // Con table splitting las columnas son columnas normales de Tasks, así que el índice
            // físico existe igual; sólo no aparece en el ModelSnapshot. Consecuencia práctica: leer
            // el .cs de la configuración NO prueba que existan, hay que consultar sys.indexes.

            // «Mis tareas» — la consulta caliente del servicio.
            migrationBuilder.Sql(
                """
                CREATE INDEX IX_Tasks_TenantId_AssigneeUserId_Status_DueAtUtc
                    ON Tasks (TenantId, AssigneeUserId, Status, DueAtUtc);
                """
            );

            // Vista por cliente y año fiscal: la firma trabaja 2025 y 2026 en paralelo (T-B1).
            migrationBuilder.Sql(
                """
                CREATE INDEX IX_Tasks_TenantId_CustomerId_TaxYear
                    ON Tasks (TenantId, CustomerId, TaxYear);
                """
            );

            // Cross-tenant a propósito: el barrido de vencidas recorre todos los tenants y sin este
            // índice es un scan completo.
            migrationBuilder.Sql(
                """
                CREATE INDEX IX_Tasks_Status_DueAtUtc
                    ON Tasks (Status, DueAtUtc);
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "Tasks");
        }
    }
}
