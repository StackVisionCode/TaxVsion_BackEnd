using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Campaigns.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCampaignRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CampaignRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CampaignId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TriggeredByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TriggerKind = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FinishedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RejectionReason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    RecipientCount = table.Column<int>(type: "int", nullable: false),
                    CounterDispatched = table.Column<int>(type: "int", nullable: false),
                    CounterAccepted = table.Column<int>(type: "int", nullable: false),
                    CounterDelivered = table.Column<int>(type: "int", nullable: false),
                    CounterFailed = table.Column<int>(type: "int", nullable: false),
                    CounterSkipped = table.Column<int>(type: "int", nullable: false),
                    CounterUnknown = table.Column<int>(type: "int", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampaignRuns", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "CampaignRecipients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContactRef = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Channel = table.Column<int>(type: "int", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    PhoneE164 = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    DispatchId = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    AttemptNo = table.Column<int>(type: "int", nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ProviderRef = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    AcceptedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeliveredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SettledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampaignRecipients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CampaignRecipients_CampaignRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "CampaignRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_CampaignRecipients_RunId_ContactRef_Channel",
                table: "CampaignRecipients",
                columns: new[] { "RunId", "ContactRef", "Channel" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_CampaignRecipients_RunId_DispatchId",
                table: "CampaignRecipients",
                columns: new[] { "RunId", "DispatchId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_CampaignRecipients_RunId_State",
                table: "CampaignRecipients",
                columns: new[] { "RunId", "State" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_CampaignRuns_TenantId_CampaignId",
                table: "CampaignRuns",
                columns: new[] { "TenantId", "CampaignId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_CampaignRuns_TenantId_Status",
                table: "CampaignRuns",
                columns: new[] { "TenantId", "Status" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "CampaignRecipients");

            migrationBuilder.DropTable(name: "CampaignRuns");
        }
    }
}
