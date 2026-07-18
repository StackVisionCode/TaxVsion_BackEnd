using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Postmaster.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSentMessageAggregate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SentMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NotificationLogId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    FromAddress = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    FromDisplayName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ReplyTo = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    Stream = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ProviderCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: false),
                    QueuedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SentAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastEventAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ErrorReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TemplateKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    RenderedHtmlChecksum = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    MimeSize = table.Column<int>(type: "int", nullable: false),
                    Metadata = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SentMessages", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "SentMessageEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SentMessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecipientId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EventType = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    EventAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RawPayload = table.Column<string>(type: "nvarchar(max)", maxLength: 8192, nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SentMessageEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SentMessageEvents_SentMessages_SentMessageId",
                        column: x => x.SentMessageId,
                        principalTable: "SentMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "SentMessageRecipients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SentMessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Address = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Type = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    ProviderMessageId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    LastEventAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ErrorReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SentMessageRecipients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SentMessageRecipients_SentMessages_SentMessageId",
                        column: x => x.SentMessageId,
                        principalTable: "SentMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_SentMessageEvents_SentMessageId_EventAtUtc",
                table: "SentMessageEvents",
                columns: new[] { "SentMessageId", "EventAtUtc" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_SentMessageRecipients_SentMessageId",
                table: "SentMessageRecipients",
                column: "SentMessageId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_SentMessageRecipients_TenantId_Address_Status",
                table: "SentMessageRecipients",
                columns: new[] { "TenantId", "Address", "Status" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_SentMessages_TenantId_IdempotencyKey",
                table: "SentMessages",
                columns: new[] { "TenantId", "IdempotencyKey" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_SentMessages_TenantId_NotificationLogId",
                table: "SentMessages",
                columns: new[] { "TenantId", "NotificationLogId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_SentMessages_TenantId_QueuedAtUtc",
                table: "SentMessages",
                columns: new[] { "TenantId", "QueuedAtUtc" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "SentMessageEvents");

            migrationBuilder.DropTable(name: "SentMessageRecipients");

            migrationBuilder.DropTable(name: "SentMessages");
        }
    }
}
