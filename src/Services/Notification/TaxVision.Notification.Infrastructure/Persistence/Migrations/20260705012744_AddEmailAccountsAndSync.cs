using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Notification.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailAccountsAndSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EmailAccountConnections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    EmailAddress = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ExternalAccountId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    AccessTokenCipher = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    RefreshTokenCipher = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    TokenExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ImapHost = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    ImapPort = table.Column<int>(type: "int", nullable: true),
                    ImapUsername = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    ImapPasswordCipher = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    ImapUseSsl = table.Column<bool>(type: "bit", nullable: false),
                    SyncStatus = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    LastSyncAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastFullSyncAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailAccountConnections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EmailFolders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExternalId = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    SyncCursor = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    LastSyncAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TotalMessages = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailFolders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EmailMessageAttachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    ExternalAttachmentId = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    CloudStorageFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailMessageAttachments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EmailSyncedMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FolderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExternalMessageId = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    ExternalThreadId = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    Subject = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    FromAddress = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    ToJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CcJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BccJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Snippet = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    BodyHtml = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    BodyText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    HeadersJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsRead = table.Column<bool>(type: "bit", nullable: false),
                    IsStarred = table.Column<bool>(type: "bit", nullable: false),
                    HasAttachments = table.Column<bool>(type: "bit", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SentAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailSyncedMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EmailSyncLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FinishedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FoldersSynced = table.Column<int>(type: "int", nullable: false),
                    MessagesSynced = table.Column<int>(type: "int", nullable: false),
                    Error = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailSyncLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmailAccountConnections_IsActive_LastSyncAtUtc",
                table: "EmailAccountConnections",
                columns: new[] { "IsActive", "LastSyncAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_EmailAccountConnections_TenantId",
                table: "EmailAccountConnections",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailFolders_AccountId_ExternalId",
                table: "EmailFolders",
                columns: new[] { "AccountId", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmailMessageAttachments_MessageId",
                table: "EmailMessageAttachments",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailSyncedMessages_AccountId_ExternalMessageId",
                table: "EmailSyncedMessages",
                columns: new[] { "AccountId", "ExternalMessageId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmailSyncedMessages_AccountId_ExternalThreadId",
                table: "EmailSyncedMessages",
                columns: new[] { "AccountId", "ExternalThreadId" });

            migrationBuilder.CreateIndex(
                name: "IX_EmailSyncedMessages_AccountId_FolderId_ReceivedAtUtc",
                table: "EmailSyncedMessages",
                columns: new[] { "AccountId", "FolderId", "ReceivedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_EmailSyncLogs_AccountId_StartedAtUtc",
                table: "EmailSyncLogs",
                columns: new[] { "AccountId", "StartedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmailAccountConnections");

            migrationBuilder.DropTable(
                name: "EmailFolders");

            migrationBuilder.DropTable(
                name: "EmailMessageAttachments");

            migrationBuilder.DropTable(
                name: "EmailSyncedMessages");

            migrationBuilder.DropTable(
                name: "EmailSyncLogs");
        }
    }
}
