using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Calendar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReminderLead : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ReminderLeadMinutes",
                table: "Appointments",
                type: "int",
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ReminderLeadMinutes", table: "Appointments");
        }
    }
}
