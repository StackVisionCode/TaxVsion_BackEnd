using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Campaigns.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCampaignRunsRecentIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_CampaignSchedules_TenantId_Status_NextFireAtUtc",
                table: "CampaignSchedules",
                columns: new[] { "TenantId", "Status", "NextFireAtUtc" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_CampaignRuns_TenantId_CreatedAtUtc",
                table: "CampaignRuns",
                columns: new[] { "TenantId", "CreatedAtUtc" },
                descending: new[] { false, true }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CampaignSchedules_TenantId_Status_NextFireAtUtc",
                table: "CampaignSchedules"
            );

            migrationBuilder.DropIndex(name: "IX_CampaignRuns_TenantId_CreatedAtUtc", table: "CampaignRuns");
        }
    }
}
