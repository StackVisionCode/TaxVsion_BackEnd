using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Tasks.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskCancellationDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                table: "Tasks",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAtUtc",
                table: "Tasks",
                type: "datetime2",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "CancelledByUserId",
                table: "Tasks",
                type: "uniqueidentifier",
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "CancellationReason", table: "Tasks");

            migrationBuilder.DropColumn(name: "CancelledAtUtc", table: "Tasks");

            migrationBuilder.DropColumn(name: "CancelledByUserId", table: "Tasks");
        }
    }
}
