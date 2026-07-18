using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Correspondence.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInboxAggregates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EmailThreads",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    ProviderThreadId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    FirstMessageAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastMessageAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    MessageCount = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ArchivedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailThreads", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "IncomingEmails",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmailThreadId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ProviderMessageId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    InternetMessageId = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    InReplyTo = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    References = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    From = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    FromDisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Subject = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Snippet = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    BodyStatus = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    BodyFetchedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    HasAttachments = table.Column<bool>(type: "bit", nullable: false),
                    AttachmentCount = table.Column<int>(type: "int", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IncomingEmails", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "IncomingEmailAttachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IncomingEmailId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Filename = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    ProviderAttachmentId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsInline = table.Column<bool>(type: "bit", nullable: false),
                    DownloadStatus = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    CloudStorageFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DownloadedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FailureReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IncomingEmailAttachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IncomingEmailAttachments_IncomingEmails_IncomingEmailId",
                        column: x => x.IncomingEmailId,
                        principalTable: "IncomingEmails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "IncomingEmailRecipients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IncomingEmailId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Address = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IncomingEmailRecipients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IncomingEmailRecipients_IncomingEmails_IncomingEmailId",
                        column: x => x.IncomingEmailId,
                        principalTable: "IncomingEmails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_EmailThreads_TenantId_CustomerId_LastMessageAtUtc",
                table: "EmailThreads",
                columns: new[] { "TenantId", "CustomerId", "LastMessageAtUtc" },
                descending: new[] { false, false, true }
            );

            migrationBuilder.CreateIndex(
                name: "IX_EmailThreads_TenantId_ProviderThreadId_Unique",
                table: "EmailThreads",
                columns: new[] { "TenantId", "ProviderThreadId" },
                unique: true,
                filter: "[ProviderThreadId] IS NOT NULL"
            );

            migrationBuilder.CreateIndex(
                name: "IX_IncomingEmailAttachments_DownloadStatus",
                table: "IncomingEmailAttachments",
                column: "DownloadStatus"
            );

            migrationBuilder.CreateIndex(
                name: "IX_IncomingEmailAttachments_IncomingEmailId",
                table: "IncomingEmailAttachments",
                column: "IncomingEmailId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_IncomingEmailRecipients_IncomingEmailId",
                table: "IncomingEmailRecipients",
                column: "IncomingEmailId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_IncomingEmailRecipients_TenantId_Address",
                table: "IncomingEmailRecipients",
                columns: new[] { "TenantId", "Address" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_IncomingEmails_TenantId_CustomerId_ReceivedAtUtc",
                table: "IncomingEmails",
                columns: new[] { "TenantId", "CustomerId", "ReceivedAtUtc" },
                descending: new[] { false, false, true }
            );

            migrationBuilder.CreateIndex(
                name: "IX_IncomingEmails_TenantId_InternetMessageId_Unique",
                table: "IncomingEmails",
                columns: new[] { "TenantId", "InternetMessageId" },
                unique: true,
                filter: "[InternetMessageId] IS NOT NULL"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "EmailThreads");

            migrationBuilder.DropTable(name: "IncomingEmailAttachments");

            migrationBuilder.DropTable(name: "IncomingEmailRecipients");

            migrationBuilder.DropTable(name: "IncomingEmails");
        }
    }
}
