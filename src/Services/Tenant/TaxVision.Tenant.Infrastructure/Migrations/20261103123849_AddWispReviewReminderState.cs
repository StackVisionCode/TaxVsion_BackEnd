using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Tenant.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWispReviewReminderState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "WispReminderDueOn",
                table: "OfficeCompliances",
                type: "date",
                nullable: true
            );

            migrationBuilder.AddColumn<DateOnly>(
                name: "WispReminderSentOn",
                table: "OfficeCompliances",
                type: "date",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "WispReminderStage",
                table: "OfficeCompliances",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "WispReminderDueOn", table: "OfficeCompliances");

            migrationBuilder.DropColumn(name: "WispReminderSentOn", table: "OfficeCompliances");

            migrationBuilder.DropColumn(name: "WispReminderStage", table: "OfficeCompliances");
        }
    }
}
