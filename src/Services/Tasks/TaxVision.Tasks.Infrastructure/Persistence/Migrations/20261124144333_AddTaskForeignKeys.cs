using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Tasks.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskForeignKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(name: "IX_Tasks_ParentTaskId", table: "Tasks", column: "ParentTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskDependencies_DependsOnTaskId",
                table: "TaskDependencies",
                column: "DependsOnTaskId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_TaskDependencies_TaskId",
                table: "TaskDependencies",
                column: "TaskId"
            );

            // Con chequeo si no hay huérfanos; si los hay, la clave frena lo nuevo y no se borra nada.
            AddForeignKeyIfClean(
                migrationBuilder,
                "TaskDependencies",
                "FK_TaskDependencies_Tasks_DependsOnTaskId",
                "DependsOnTaskId"
            );
            AddForeignKeyIfClean(migrationBuilder, "TaskDependencies", "FK_TaskDependencies_Tasks_TaskId", "TaskId");
            AddForeignKeyIfClean(migrationBuilder, "Tasks", "FK_Tasks_Tasks_ParentTaskId", "ParentTaskId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TaskDependencies_Tasks_DependsOnTaskId",
                table: "TaskDependencies"
            );

            migrationBuilder.DropForeignKey(name: "FK_TaskDependencies_Tasks_TaskId", table: "TaskDependencies");

            migrationBuilder.DropForeignKey(name: "FK_Tasks_Tasks_ParentTaskId", table: "Tasks");

            migrationBuilder.DropIndex(name: "IX_Tasks_ParentTaskId", table: "Tasks");

            migrationBuilder.DropIndex(name: "IX_TaskDependencies_DependsOnTaskId", table: "TaskDependencies");

            migrationBuilder.DropIndex(name: "IX_TaskDependencies_TaskId", table: "TaskDependencies");
        }

        private static void AddForeignKeyIfClean(
            MigrationBuilder migrationBuilder,
            string table,
            string name,
            string column
        ) =>
            migrationBuilder.Sql(
                $"""
                IF EXISTS (SELECT 1 FROM [{table}] c WHERE c.[{column}] IS NOT NULL
                    AND NOT EXISTS (SELECT 1 FROM [Tasks] t WHERE t.[Id] = c.[{column}]))
                    ALTER TABLE [{table}] WITH NOCHECK ADD CONSTRAINT [{name}] FOREIGN KEY ([{column}]) REFERENCES [Tasks] ([Id]);
                ELSE
                    ALTER TABLE [{table}] WITH CHECK ADD CONSTRAINT [{name}] FOREIGN KEY ([{column}]) REFERENCES [Tasks] ([Id]);
                """
            );
    }
}
