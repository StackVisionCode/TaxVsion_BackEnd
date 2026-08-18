using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Tasks.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskTemplateRecurrence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RecurrenceMode",
                table: "TaskTemplates",
                type: "int",
                nullable: false,
                defaultValue: 0
            );

            migrationBuilder.AddColumn<string>(
                name: "RecurrenceRule",
                table: "TaskTemplates",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "RecurrenceTimeZoneId",
                table: "TaskTemplates",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "RecurrenceMode", table: "TaskTemplates");

            migrationBuilder.DropColumn(name: "RecurrenceRule", table: "TaskTemplates");

            migrationBuilder.DropColumn(name: "RecurrenceTimeZoneId", table: "TaskTemplates");
        }
    }
}
