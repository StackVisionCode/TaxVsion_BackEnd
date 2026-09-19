using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Campaigns.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCampaignSchedules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CampaignSchedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CampaignId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    NextFireAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IntervalMinutes = table.Column<int>(type: "int", nullable: true),
                    ContactListIdsCsv = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    LastFiredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ActiveRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LeaseToken = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LeasedUntilUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampaignSchedules", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_CampaignSchedules_ActiveRunId",
                table: "CampaignSchedules",
                column: "ActiveRunId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_CampaignSchedules_Status_NextFireAtUtc",
                table: "CampaignSchedules",
                columns: new[] { "Status", "NextFireAtUtc" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_CampaignSchedules_TenantId_CampaignId",
                table: "CampaignSchedules",
                columns: new[] { "TenantId", "CampaignId" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "CampaignSchedules");
        }
    }
}
