using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Campaigns.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduleRecurrenceEnd : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "EndsAtUtc",
                table: "CampaignSchedules",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Frequency",
                table: "CampaignSchedules",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxOccurrences",
                table: "CampaignSchedules",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OccurrenceCount",
                table: "CampaignSchedules",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EndsAtUtc",
                table: "CampaignSchedules");

            migrationBuilder.DropColumn(
                name: "Frequency",
                table: "CampaignSchedules");

            migrationBuilder.DropColumn(
                name: "MaxOccurrences",
                table: "CampaignSchedules");

            migrationBuilder.DropColumn(
                name: "OccurrenceCount",
                table: "CampaignSchedules");
        }
    }
}
