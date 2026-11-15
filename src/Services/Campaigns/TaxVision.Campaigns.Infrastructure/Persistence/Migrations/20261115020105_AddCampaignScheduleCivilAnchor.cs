using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Campaigns.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCampaignScheduleCivilAnchor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(name: "AnchorDay", table: "CampaignSchedules", type: "int", nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "LocalTimeOfDay",
                table: "CampaignSchedules",
                type: "time",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "TimeZoneId",
                table: "CampaignSchedules",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "AnchorDay", table: "CampaignSchedules");

            migrationBuilder.DropColumn(name: "LocalTimeOfDay", table: "CampaignSchedules");

            migrationBuilder.DropColumn(name: "TimeZoneId", table: "CampaignSchedules");
        }
    }
}
