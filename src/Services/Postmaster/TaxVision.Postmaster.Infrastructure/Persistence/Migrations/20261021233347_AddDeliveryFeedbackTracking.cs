using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Postmaster.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDeliveryFeedbackTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DeliveryLane",
                table: "SentMessages",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "SystemProvider"
            );

            // Los envíos viejos por buzón se reconocen por su scope (ahí se guardaba por dónde salió).
            migrationBuilder.Sql(
                "UPDATE SentMessages SET DeliveryLane = 'TenantMailbox' WHERE RequiredProviderScope = 'TenantMailbox';"
            );

            migrationBuilder.AddColumn<int>(
                name: "ClickCount",
                table: "SentMessageRecipients",
                type: "int",
                nullable: false,
                defaultValue: 0
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "FirstClickedAtUtc",
                table: "SentMessageRecipients",
                type: "datetime2",
                nullable: true
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "FirstOpenedAtUtc",
                table: "SentMessageRecipients",
                type: "datetime2",
                nullable: true
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "LastOpenedAtUtc",
                table: "SentMessageRecipients",
                type: "datetime2",
                nullable: true
            );

            migrationBuilder.AddColumn<int>(
                name: "OpenCount",
                table: "SentMessageRecipients",
                type: "int",
                nullable: false,
                defaultValue: 0
            );

            migrationBuilder.AddColumn<string>(
                name: "Outcome",
                table: "SentMessageRecipients",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "OutcomeAtUtc",
                table: "SentMessageRecipients",
                type: "datetime2",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "ProviderAcceptance",
                table: "SentMessageRecipients",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "UnsubscribedAtUtc",
                table: "SentMessageRecipients",
                type: "datetime2",
                nullable: true
            );

            migrationBuilder.CreateTable(
                name: "DeliveryFeedbackReceipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    SourceEventId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SentMessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliveryFeedbackReceipts", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "UnmatchedDeliveryFeedback",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    SourceEventId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    MaskedRecipient = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    CorrelationHint = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RawPayload = table.Column<string>(type: "nvarchar(max)", maxLength: 8192, nullable: true),
                    ReceivedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UnmatchedDeliveryFeedback", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_SentMessageRecipients_ProviderMessageId",
                table: "SentMessageRecipients",
                column: "ProviderMessageId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryFeedbackReceipts_SentMessageId",
                table: "DeliveryFeedbackReceipts",
                column: "SentMessageId"
            );

            migrationBuilder.CreateIndex(
                name: "UX_DeliveryFeedbackReceipts_Source_SourceEventId",
                table: "DeliveryFeedbackReceipts",
                columns: new[] { "Source", "SourceEventId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_UnmatchedDeliveryFeedback_ReceivedAtUtc",
                table: "UnmatchedDeliveryFeedback",
                column: "ReceivedAtUtc"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "DeliveryFeedbackReceipts");

            migrationBuilder.DropTable(name: "UnmatchedDeliveryFeedback");

            migrationBuilder.DropIndex(
                name: "IX_SentMessageRecipients_ProviderMessageId",
                table: "SentMessageRecipients"
            );

            migrationBuilder.DropColumn(name: "DeliveryLane", table: "SentMessages");

            migrationBuilder.DropColumn(name: "ClickCount", table: "SentMessageRecipients");

            migrationBuilder.DropColumn(name: "FirstClickedAtUtc", table: "SentMessageRecipients");

            migrationBuilder.DropColumn(name: "FirstOpenedAtUtc", table: "SentMessageRecipients");

            migrationBuilder.DropColumn(name: "LastOpenedAtUtc", table: "SentMessageRecipients");

            migrationBuilder.DropColumn(name: "OpenCount", table: "SentMessageRecipients");

            migrationBuilder.DropColumn(name: "Outcome", table: "SentMessageRecipients");

            migrationBuilder.DropColumn(name: "OutcomeAtUtc", table: "SentMessageRecipients");

            migrationBuilder.DropColumn(name: "ProviderAcceptance", table: "SentMessageRecipients");

            migrationBuilder.DropColumn(name: "UnsubscribedAtUtc", table: "SentMessageRecipients");
        }
    }
}
