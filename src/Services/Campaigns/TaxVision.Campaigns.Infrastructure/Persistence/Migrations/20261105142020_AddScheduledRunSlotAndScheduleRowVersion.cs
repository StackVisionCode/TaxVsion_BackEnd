using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Campaigns.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduledRunSlotAndScheduleRowVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "CampaignSchedules",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<Guid>(
                name: "ScheduleId",
                table: "CampaignRuns",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ScheduledSlotUtc",
                table: "CampaignRuns",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CampaignRuns_ScheduleId_ScheduledSlotUtc_Unique",
                table: "CampaignRuns",
                columns: new[] { "ScheduleId", "ScheduledSlotUtc" },
                unique: true,
                filter: "[ScheduleId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CampaignRuns_ScheduleId_ScheduledSlotUtc_Unique",
                table: "CampaignRuns");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "CampaignSchedules");

            migrationBuilder.DropColumn(
                name: "ScheduleId",
                table: "CampaignRuns");

            migrationBuilder.DropColumn(
                name: "ScheduledSlotUtc",
                table: "CampaignRuns");
        }
    }
}
