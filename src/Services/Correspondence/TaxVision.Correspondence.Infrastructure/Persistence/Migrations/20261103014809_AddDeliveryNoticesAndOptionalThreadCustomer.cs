using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Correspondence.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDeliveryNoticesAndOptionalThreadCustomer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "CustomerId",
                table: "EmailThreads",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier"
            );

            migrationBuilder.AddColumn<string>(
                name: "DeliveryCategory",
                table: "DraftRecipients",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "DeliveryStatus",
                table: "DraftRecipients",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "DeliveryUpdatedAtUtc",
                table: "DraftRecipients",
                type: "datetime2",
                nullable: true
            );

            migrationBuilder.CreateTable(
                name: "DeliveryNotices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmailThreadId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DraftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SentMessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Recipient = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    StatusCode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    Diagnostic = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Suppressed = table.Column<bool>(type: "bit", nullable: false),
                    ReportAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReportProviderMessageId = table.Column<string>(
                        type: "nvarchar(500)",
                        maxLength: 500,
                        nullable: true
                    ),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsRead = table.Column<bool>(type: "bit", nullable: false),
                    ReadAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliveryNotices", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryNotices_TenantId_EmailThreadId_OccurredAtUtc",
                table: "DeliveryNotices",
                columns: new[] { "TenantId", "EmailThreadId", "OccurredAtUtc" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryNotices_TenantId_EmailThreadId_Unread",
                table: "DeliveryNotices",
                columns: new[] { "TenantId", "EmailThreadId" },
                filter: "[IsRead] = 0"
            );

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryNotices_TenantId_SentMessageId_Recipient_Outcome_Unique",
                table: "DeliveryNotices",
                columns: new[] { "TenantId", "SentMessageId", "Recipient", "Outcome" },
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "DeliveryNotices");

            migrationBuilder.DropColumn(name: "DeliveryCategory", table: "DraftRecipients");

            migrationBuilder.DropColumn(name: "DeliveryStatus", table: "DraftRecipients");

            migrationBuilder.DropColumn(name: "DeliveryUpdatedAtUtc", table: "DraftRecipients");

            migrationBuilder.AlterColumn<Guid>(
                name: "CustomerId",
                table: "EmailThreads",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true
            );
        }
    }
}
