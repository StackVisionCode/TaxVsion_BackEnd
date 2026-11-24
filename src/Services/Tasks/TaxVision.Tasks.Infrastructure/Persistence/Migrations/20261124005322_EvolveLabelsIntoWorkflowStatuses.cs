using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Tasks.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EvolveLabelsIntoWorkflowStatuses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // La tabla de labels evoluciona: se renombra, no se recrea (las filas se conservan).
            migrationBuilder.RenameTable(name: "TaskLabels", newName: "TaskWorkflowStatuses");
            migrationBuilder.Sql("EXEC sp_rename N'PK_TaskLabels', N'PK_TaskWorkflowStatuses', N'OBJECT'");
            migrationBuilder.RenameColumn(name: "MapsToStatus", table: "TaskWorkflowStatuses", newName: "Category");
            migrationBuilder.RenameIndex(
                name: "UX_TaskLabels_TenantId_Code",
                table: "TaskWorkflowStatuses",
                newName: "UX_TaskWorkflowStatuses_TenantId_Code"
            );

            migrationBuilder.AddColumn<bool>(
                name: "IsDefault",
                table: "TaskWorkflowStatuses",
                type: "bit",
                nullable: false,
                defaultValue: false
            );
            migrationBuilder.AddColumn<bool>(
                name: "IsArchived",
                table: "TaskWorkflowStatuses",
                type: "bit",
                nullable: false,
                defaultValue: false
            );
            migrationBuilder.AddColumn<DateTime>(
                name: "ArchivedAtUtc",
                table: "TaskWorkflowStatuses",
                type: "datetime2",
                nullable: true
            );
            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "TaskWorkflowStatuses",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true
            );
            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "TaskWorkflowStatuses",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]
            );
            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                table: "TaskWorkflowStatuses",
                type: "uniqueidentifier",
                nullable: true
            );
            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAtUtc",
                table: "TaskWorkflowStatuses",
                type: "datetime2",
                nullable: true
            );
            migrationBuilder.AddColumn<Guid>(
                name: "UpdatedByUserId",
                table: "TaskWorkflowStatuses",
                type: "uniqueidentifier",
                nullable: true
            );
            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAtUtc",
                table: "TaskWorkflowStatuses",
                type: "datetime2",
                nullable: true
            );

            // Datos, antes de los índices únicos. Columnas recién creadas: van por sp_executesql.
            // 1) Nombres repetidos en una oficina: queda activa la primera; las demás se archivan (no se borran).
            migrationBuilder.Sql(
                @"EXEC sp_executesql N'
                WITH ranked AS (
                    SELECT IsArchived, ArchivedAtUtc,
                           ROW_NUMBER() OVER (PARTITION BY TenantId, DisplayName ORDER BY SortOrder, DisplayName, Id) AS rn
                    FROM TaskWorkflowStatuses)
                UPDATE ranked SET IsArchived = 1, ArchivedAtUtc = SYSUTCDATETIME() WHERE rn > 1;'"
            );
            // 2) Un código del sistema en otra etapa no puede representar al estado del sistema.
            migrationBuilder.Sql(
                @"EXEC sp_executesql N'
                UPDATE TaskWorkflowStatuses SET IsArchived = 1, ArchivedAtUtc = SYSUTCDATETIME()
                WHERE IsArchived = 0 AND (
                       (Code = ''not_started'' AND Category <> 1) OR (Code = ''in_progress'' AND Category <> 2)
                    OR (Code = ''waiting_on_client'' AND Category <> 3) OR (Code = ''completed'' AND Category <> 4)
                    OR (Code = ''cancelled'' AND Category <> 5));'"
            );
            // 3) Completed y Cancelled tienen un solo estado: el primero queda, los demás se archivan.
            migrationBuilder.Sql(
                @"EXEC sp_executesql N'
                WITH ranked AS (
                    SELECT IsArchived, ArchivedAtUtc,
                           ROW_NUMBER() OVER (PARTITION BY TenantId, Category ORDER BY SortOrder, DisplayName, Id) AS rn
                    FROM TaskWorkflowStatuses WHERE Category IN (4, 5) AND IsArchived = 0)
                UPDATE ranked SET IsArchived = 1, ArchivedAtUtc = SYSUTCDATETIME() WHERE rn > 1;'"
            );
            // 4) Por oficina y etapa, la primera label activa pasa a ser el estado por defecto: lo que hoy pinta el CRM.
            migrationBuilder.Sql(
                @"EXEC sp_executesql N'
                WITH ranked AS (
                    SELECT IsDefault,
                           ROW_NUMBER() OVER (PARTITION BY TenantId, Category ORDER BY SortOrder, DisplayName, Id) AS rn
                    FROM TaskWorkflowStatuses WHERE IsArchived = 0)
                UPDATE ranked SET IsDefault = 1 WHERE rn = 1;'"
            );

            migrationBuilder.CreateIndex(
                name: "UX_TaskWorkflowStatuses_TenantId_Category_Default",
                table: "TaskWorkflowStatuses",
                columns: new[] { "TenantId", "Category" },
                unique: true,
                filter: "[IsDefault] = 1 AND [IsArchived] = 0"
            );
            migrationBuilder.CreateIndex(
                name: "UX_TaskWorkflowStatuses_TenantId_DisplayName_Active",
                table: "TaskWorkflowStatuses",
                columns: new[] { "TenantId", "DisplayName" },
                unique: true,
                filter: "[IsArchived] = 0"
            );

            // La tarea: estado de la oficina anulable (null = por defecto de su etapa). No se reescribe ninguna fila.
            migrationBuilder.AddColumn<string>(
                name: "WorkflowStatusCode",
                table: "Tasks",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true
            );
            migrationBuilder.AddColumn<Guid>(
                name: "WorkflowStatusId",
                table: "Tasks",
                type: "uniqueidentifier",
                nullable: true
            );
            migrationBuilder.CreateIndex(
                name: "IX_Tasks_TenantId_WorkflowStatusId",
                table: "Tasks",
                columns: new[] { "TenantId", "WorkflowStatusId" },
                filter: "[WorkflowStatusId] IS NOT NULL"
            );
            migrationBuilder.CreateIndex(name: "IX_Tasks_WorkflowStatusId", table: "Tasks", column: "WorkflowStatusId");
            migrationBuilder.AddForeignKey(
                name: "FK_Tasks_TaskWorkflowStatuses_WorkflowStatusId",
                table: "Tasks",
                column: "WorkflowStatusId",
                principalTable: "TaskWorkflowStatuses",
                principalColumn: "Id"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(name: "FK_Tasks_TaskWorkflowStatuses_WorkflowStatusId", table: "Tasks");
            migrationBuilder.DropIndex(name: "IX_Tasks_TenantId_WorkflowStatusId", table: "Tasks");
            migrationBuilder.DropIndex(name: "IX_Tasks_WorkflowStatusId", table: "Tasks");
            migrationBuilder.DropColumn(name: "WorkflowStatusCode", table: "Tasks");
            migrationBuilder.DropColumn(name: "WorkflowStatusId", table: "Tasks");

            migrationBuilder.DropIndex(
                name: "UX_TaskWorkflowStatuses_TenantId_Category_Default",
                table: "TaskWorkflowStatuses"
            );
            migrationBuilder.DropIndex(
                name: "UX_TaskWorkflowStatuses_TenantId_DisplayName_Active",
                table: "TaskWorkflowStatuses"
            );
            foreach (
                var column in new[]
                {
                    "IsDefault",
                    "IsArchived",
                    "ArchivedAtUtc",
                    "Description",
                    "RowVersion",
                    "CreatedByUserId",
                    "CreatedAtUtc",
                    "UpdatedByUserId",
                    "UpdatedAtUtc",
                }
            )
                migrationBuilder.DropColumn(name: column, table: "TaskWorkflowStatuses");

            migrationBuilder.RenameIndex(
                name: "UX_TaskWorkflowStatuses_TenantId_Code",
                table: "TaskWorkflowStatuses",
                newName: "UX_TaskLabels_TenantId_Code"
            );
            migrationBuilder.RenameColumn(name: "Category", table: "TaskWorkflowStatuses", newName: "MapsToStatus");
            migrationBuilder.Sql("EXEC sp_rename N'PK_TaskWorkflowStatuses', N'PK_TaskLabels', N'OBJECT'");
            migrationBuilder.RenameTable(name: "TaskWorkflowStatuses", newName: "TaskLabels");
        }
    }
}
