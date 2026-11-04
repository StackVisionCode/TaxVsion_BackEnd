using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Campaigns.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduleRunAsAndHandovers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RunAsUserId",
                table: "CampaignSchedules",
                type: "uniqueidentifier",
                nullable: true
            );

            migrationBuilder.CreateTable(
                name: "CampaignScheduleHandovers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScheduleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CampaignId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FromUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ToUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ToCanViewAllCustomers = table.Column<bool>(type: "bit", nullable: true),
                    Outcome = table.Column<int>(type: "int", nullable: false),
                    PerformedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourceEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampaignScheduleHandovers", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_CampaignScheduleHandovers_TenantId_FromUserId",
                table: "CampaignScheduleHandovers",
                columns: new[] { "TenantId", "FromUserId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_CampaignScheduleHandovers_TenantId_ScheduleId",
                table: "CampaignScheduleHandovers",
                columns: new[] { "TenantId", "ScheduleId" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "CampaignScheduleHandovers");

            migrationBuilder.DropColumn(name: "RunAsUserId", table: "CampaignSchedules");
        }
    }
}
