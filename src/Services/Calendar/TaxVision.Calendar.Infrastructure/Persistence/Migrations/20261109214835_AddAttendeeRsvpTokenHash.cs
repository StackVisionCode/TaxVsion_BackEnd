using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Calendar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendeeRsvpTokenHash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "RsvpTokenHash",
                table: "AppointmentAttendees",
                type: "varbinary(32)",
                nullable: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentAttendees_RsvpTokenHash",
                table: "AppointmentAttendees",
                column: "RsvpTokenHash",
                unique: true,
                filter: "[RsvpTokenHash] IS NOT NULL"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_AppointmentAttendees_RsvpTokenHash", table: "AppointmentAttendees");

            migrationBuilder.DropColumn(name: "RsvpTokenHash", table: "AppointmentAttendees");
        }
    }
}
