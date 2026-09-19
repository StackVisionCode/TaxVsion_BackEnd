using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Notification.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DropEmailCampaigns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "EmailCampaignRecipients");

            migrationBuilder.DropTable(name: "EmailCampaigns");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EmailCampaigns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AllowedVariablesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ClickedCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FailedCount = table.Column<int>(type: "int", nullable: false),
                    FinishedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    HtmlTemplate = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LayoutHtml = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    OpenedCount = table.Column<int>(type: "int", nullable: false),
                    ScheduledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SentCount = table.Column<int>(type: "int", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    SubjectTemplate = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TotalRecipients = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailCampaigns", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "EmailCampaignRecipients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Address = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    CampaignId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    VariablesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailCampaignRecipients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmailCampaignRecipients_EmailCampaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "EmailCampaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_EmailCampaignRecipients_CampaignId",
                table: "EmailCampaignRecipients",
                column: "CampaignId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_EmailCampaigns_Status_ScheduledAtUtc",
                table: "EmailCampaigns",
                columns: new[] { "Status", "ScheduledAtUtc" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_EmailCampaigns_TenantId_Status",
                table: "EmailCampaigns",
                columns: new[] { "TenantId", "Status" }
            );
        }
    }
}
