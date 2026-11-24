using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Tasks.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskTrash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAtUtc",
                table: "Tasks",
                type: "datetime2",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedByUserId",
                table: "Tasks",
                type: "uniqueidentifier",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "TrashRootId",
                table: "Tasks",
                type: "uniqueidentifier",
                nullable: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_DeletedAtUtc",
                table: "Tasks",
                column: "DeletedAtUtc",
                filter: "[DeletedAtUtc] IS NOT NULL"
            );

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_TenantId_TrashRootId",
                table: "Tasks",
                columns: new[] { "TenantId", "TrashRootId" },
                filter: "[TrashRootId] IS NOT NULL"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_Tasks_DeletedAtUtc", table: "Tasks");

            migrationBuilder.DropIndex(name: "IX_Tasks_TenantId_TrashRootId", table: "Tasks");

            migrationBuilder.DropColumn(name: "DeletedAtUtc", table: "Tasks");

            migrationBuilder.DropColumn(name: "DeletedByUserId", table: "Tasks");

            migrationBuilder.DropColumn(name: "TrashRootId", table: "Tasks");
        }
    }
}
