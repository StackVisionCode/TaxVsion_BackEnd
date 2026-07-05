using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Notification.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboundEmailMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OutboundEmailMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    HtmlBody = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TextBody = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TemplateVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CampaignId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Priority = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ConfigurationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ProviderType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    AttachmentFileIdsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RetryCount = table.Column<int>(type: "int", nullable: false),
                    MaxRetries = table.Column<int>(type: "int", nullable: false),
                    Error = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SentAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeliveredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    OpenedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClickedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    BouncedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboundEmailMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EmailDeliveryLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Detail = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    AttemptedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailDeliveryLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmailDeliveryLogs_OutboundEmailMessages_MessageId",
                        column: x => x.MessageId,
                        principalTable: "OutboundEmailMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EmailRecipients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Address = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    OpenedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClickedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    BouncedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailRecipients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmailRecipients_OutboundEmailMessages_MessageId",
                        column: x => x.MessageId,
                        principalTable: "OutboundEmailMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmailDeliveryLogs_MessageId",
                table: "EmailDeliveryLogs",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailRecipients_MessageId",
                table: "EmailRecipients",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_OutboundEmailMessages_CampaignId",
                table: "OutboundEmailMessages",
                column: "CampaignId");

            migrationBuilder.CreateIndex(
                name: "IX_OutboundEmailMessages_TenantId_CreatedAtUtc",
                table: "OutboundEmailMessages",
                columns: new[] { "TenantId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_OutboundEmailMessages_TenantId_Status",
                table: "OutboundEmailMessages",
                columns: new[] { "TenantId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmailDeliveryLogs");

            migrationBuilder.DropTable(
                name: "EmailRecipients");

            migrationBuilder.DropTable(
                name: "OutboundEmailMessages");
        }
    }
}
