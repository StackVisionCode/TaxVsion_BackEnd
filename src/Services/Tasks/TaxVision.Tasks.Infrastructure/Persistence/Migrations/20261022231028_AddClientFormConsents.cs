using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Tasks.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddClientFormConsents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "EntryNoticeId",
                table: "FormIntakeLinks",
                type: "uniqueidentifier",
                nullable: true
            );

            migrationBuilder.AddColumn<int>(
                name: "EntryNoticeVersionNumber",
                table: "FormIntakeLinks",
                type: "int",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "EntryNoticeId",
                table: "FormAccessGrants",
                type: "uniqueidentifier",
                nullable: true
            );

            migrationBuilder.AddColumn<int>(
                name: "EntryNoticeVersionNumber",
                table: "FormAccessGrants",
                type: "int",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "EntryNoticeContentHash",
                table: "FormAccessChallenges",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "EntryNoticeId",
                table: "FormAccessChallenges",
                type: "uniqueidentifier",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "EntryNoticeLanguage",
                table: "FormAccessChallenges",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "EntryNoticeTextSnapshot",
                table: "FormAccessChallenges",
                type: "nvarchar(max)",
                maxLength: 20000,
                nullable: true
            );

            migrationBuilder.AddColumn<int>(
                name: "EntryNoticeVersionNumber",
                table: "FormAccessChallenges",
                type: "int",
                nullable: true
            );

            migrationBuilder.CreateTable(
                name: "ConsentNotices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Purpose = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    LatestVersionNumber = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ArchivedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ArchivedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsentNotices", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "ConsentNoticeVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConsentNoticeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionNumber = table.Column<int>(type: "int", nullable: false),
                    Language = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    BodyJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TextSnapshot = table.Column<string>(type: "nvarchar(max)", maxLength: 20000, nullable: false),
                    ContentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PublishedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PublishedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsentNoticeVersions", x => x.Id);
                    table.CheckConstraint("CK_ConsentNoticeVersions_Body_IsJson", "ISJSON([BodyJson]) = 1");
                    table.ForeignKey(
                        name: "FK_ConsentNoticeVersions_ConsentNotices_ConsentNoticeId",
                        column: x => x.ConsentNoticeId,
                        principalTable: "ConsentNotices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "FormConsentRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FormRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionNumber = table.Column<int>(type: "int", nullable: true),
                    ComponentId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ParticipantKind = table.Column<int>(type: "int", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ParticipantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AcceptedByKind = table.Column<int>(type: "int", nullable: false),
                    AcceptedByPortalUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AcceptedByParticipantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AcceptedBySessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ConsentNoticeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NoticeVersionNumber = table.Column<int>(type: "int", nullable: false),
                    NoticeContentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TextSnapshot = table.Column<string>(type: "nvarchar(max)", maxLength: 20000, nullable: false),
                    Language = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    AcceptanceText = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Outcome = table.Column<int>(type: "int", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ClientIp = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    UserAgent = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormConsentRecords", x => x.Id);
                    table.CheckConstraint(
                        "CK_FormConsentRecords_AcceptedBy",
                        "([AcceptedByKind] = 1 AND [AcceptedByPortalUserId] IS NOT NULL AND [AcceptedByParticipantId] IS NULL AND [AcceptedBySessionId] IS NULL) OR ([AcceptedByKind] = 2 AND [AcceptedByPortalUserId] IS NULL AND [AcceptedByParticipantId] IS NOT NULL AND [AcceptedBySessionId] IS NOT NULL)"
                    );
                    table.CheckConstraint(
                        "CK_FormConsentRecords_Participant",
                        "([ParticipantKind] = 1 AND [CustomerId] IS NOT NULL AND [ParticipantId] IS NULL) OR ([ParticipantKind] = 2 AND [CustomerId] IS NULL AND [ParticipantId] IS NOT NULL)"
                    );
                    table.ForeignKey(
                        name: "FK_FormConsentRecords_ConsentNotices_ConsentNoticeId",
                        column: x => x.ConsentNoticeId,
                        principalTable: "ConsentNotices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                    table.ForeignKey(
                        name: "FK_FormConsentRecords_FormRequests_FormRequestId",
                        column: x => x.FormRequestId,
                        principalTable: "FormRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_FormIntakeLinks_EntryNoticeId",
                table: "FormIntakeLinks",
                column: "EntryNoticeId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_FormAccessGrants_EntryNoticeId",
                table: "FormAccessGrants",
                column: "EntryNoticeId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_FormAccessChallenges_EntryNoticeId",
                table: "FormAccessChallenges",
                column: "EntryNoticeId"
            );

            migrationBuilder.CreateIndex(
                name: "UX_ConsentNotices_Tenant_Key",
                table: "ConsentNotices",
                columns: new[] { "TenantId", "Key" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_ConsentNoticeVersions_ConsentNoticeId",
                table: "ConsentNoticeVersions",
                column: "ConsentNoticeId"
            );

            migrationBuilder.CreateIndex(
                name: "UX_ConsentNoticeVersions_Tenant_Notice_Version",
                table: "ConsentNoticeVersions",
                columns: new[] { "TenantId", "ConsentNoticeId", "VersionNumber" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_FormConsentRecords_ConsentNoticeId",
                table: "FormConsentRecords",
                column: "ConsentNoticeId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_FormConsentRecords_FormRequestId",
                table: "FormConsentRecords",
                column: "FormRequestId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_FormConsentRecords_Request_Outcome",
                table: "FormConsentRecords",
                columns: new[] { "TenantId", "FormRequestId", "Outcome" }
            );

            migrationBuilder.CreateIndex(
                name: "UX_FormConsentRecords_Evidence",
                table: "FormConsentRecords",
                columns: new[]
                {
                    "TenantId",
                    "FormRequestId",
                    "ComponentId",
                    "ConsentNoticeId",
                    "NoticeVersionNumber",
                    "Outcome",
                },
                unique: true
            );

            migrationBuilder.AddForeignKey(
                name: "FK_FormAccessChallenges_ConsentNotices_EntryNoticeId",
                table: "FormAccessChallenges",
                column: "EntryNoticeId",
                principalTable: "ConsentNotices",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict
            );

            migrationBuilder.AddForeignKey(
                name: "FK_FormAccessGrants_ConsentNotices_EntryNoticeId",
                table: "FormAccessGrants",
                column: "EntryNoticeId",
                principalTable: "ConsentNotices",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict
            );

            migrationBuilder.AddForeignKey(
                name: "FK_FormIntakeLinks_ConsentNotices_EntryNoticeId",
                table: "FormIntakeLinks",
                column: "EntryNoticeId",
                principalTable: "ConsentNotices",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_FormAccessChallenges_ConsentNotices_EntryNoticeId",
                table: "FormAccessChallenges"
            );

            migrationBuilder.DropForeignKey(
                name: "FK_FormAccessGrants_ConsentNotices_EntryNoticeId",
                table: "FormAccessGrants"
            );

            migrationBuilder.DropForeignKey(
                name: "FK_FormIntakeLinks_ConsentNotices_EntryNoticeId",
                table: "FormIntakeLinks"
            );

            migrationBuilder.DropTable(name: "ConsentNoticeVersions");

            migrationBuilder.DropTable(name: "FormConsentRecords");

            migrationBuilder.DropTable(name: "ConsentNotices");

            migrationBuilder.DropIndex(name: "IX_FormIntakeLinks_EntryNoticeId", table: "FormIntakeLinks");

            migrationBuilder.DropIndex(name: "IX_FormAccessGrants_EntryNoticeId", table: "FormAccessGrants");

            migrationBuilder.DropIndex(name: "IX_FormAccessChallenges_EntryNoticeId", table: "FormAccessChallenges");

            migrationBuilder.DropColumn(name: "EntryNoticeId", table: "FormIntakeLinks");

            migrationBuilder.DropColumn(name: "EntryNoticeVersionNumber", table: "FormIntakeLinks");

            migrationBuilder.DropColumn(name: "EntryNoticeId", table: "FormAccessGrants");

            migrationBuilder.DropColumn(name: "EntryNoticeVersionNumber", table: "FormAccessGrants");

            migrationBuilder.DropColumn(name: "EntryNoticeContentHash", table: "FormAccessChallenges");

            migrationBuilder.DropColumn(name: "EntryNoticeId", table: "FormAccessChallenges");

            migrationBuilder.DropColumn(name: "EntryNoticeLanguage", table: "FormAccessChallenges");

            migrationBuilder.DropColumn(name: "EntryNoticeTextSnapshot", table: "FormAccessChallenges");

            migrationBuilder.DropColumn(name: "EntryNoticeVersionNumber", table: "FormAccessChallenges");
        }
    }
}
