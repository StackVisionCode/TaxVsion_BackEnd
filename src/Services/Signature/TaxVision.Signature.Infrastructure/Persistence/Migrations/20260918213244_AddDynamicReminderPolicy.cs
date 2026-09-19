using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Signature.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDynamicReminderPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DefaultReminderIntervalHoursValue",
                table: "TenantSignatureSettings",
                type: "int",
                nullable: false,
                defaultValue: 48
            );

            migrationBuilder.AddColumn<bool>(
                name: "AutoRemindersEnabled",
                table: "SignatureRequests",
                type: "bit",
                nullable: false,
                defaultValue: true
            );

            migrationBuilder.AddColumn<int>(
                name: "ReminderIntervalHours",
                table: "SignatureRequests",
                type: "int",
                nullable: false,
                defaultValue: 48
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "DefaultReminderIntervalHoursValue", table: "TenantSignatureSettings");

            migrationBuilder.DropColumn(name: "AutoRemindersEnabled", table: "SignatureRequests");

            migrationBuilder.DropColumn(name: "ReminderIntervalHours", table: "SignatureRequests");
        }
    }
}
