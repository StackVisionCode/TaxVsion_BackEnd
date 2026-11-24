using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Tasks.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskTimeEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsManual",
                table: "TaskTimers",
                type: "bit",
                nullable: false,
                defaultValue: false
            );

            migrationBuilder.AddColumn<string>(
                name: "Note",
                table: "TaskTimers",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAtUtc",
                table: "TaskTimers",
                type: "datetime2",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "UpdatedByUserId",
                table: "TaskTimers",
                type: "uniqueidentifier",
                nullable: true
            );

            migrationBuilder.CreateIndex(
                name: "UX_TaskTimers_Running",
                table: "TaskTimers",
                columns: new[] { "TaskId", "UserId" },
                unique: true,
                filter: "[StoppedAtUtc] IS NULL"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "UX_TaskTimers_Running", table: "TaskTimers");

            migrationBuilder.DropColumn(name: "IsManual", table: "TaskTimers");

            migrationBuilder.DropColumn(name: "Note", table: "TaskTimers");

            migrationBuilder.DropColumn(name: "UpdatedAtUtc", table: "TaskTimers");

            migrationBuilder.DropColumn(name: "UpdatedByUserId", table: "TaskTimers");
        }
    }
}
