using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Tasks.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddClientFormExternalAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FormAccessGrants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FormRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TokenLast4 = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastUsedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IssueCount = table.Column<int>(type: "int", nullable: false),
                    IssuedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormAccessGrants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FormAccessGrants_FormRequests_FormRequestId",
                        column: x => x.FormRequestId,
                        principalTable: "FormRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "FormIntakeLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    FormTemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FormTemplateVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    TokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TokenLast4 = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: false),
                    TokenCipher = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    TokenKeyVersion = table.Column<short>(type: "smallint", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    MaxSubmissions = table.Column<int>(type: "int", nullable: true),
                    SubmissionCount = table.Column<int>(type: "int", nullable: false),
                    ResponsibleUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormIntakeLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FormIntakeLinks_FormTemplateVersions_FormTemplateVersionId",
                        column: x => x.FormTemplateVersionId,
                        principalTable: "FormTemplateVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                    table.ForeignKey(
                        name: "FK_FormIntakeLinks_FormTemplates_FormTemplateId",
                        column: x => x.FormTemplateId,
                        principalTable: "FormTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "FormParticipants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PhoneE164 = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    VerifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LinkedCustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LinkedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LinkedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Source = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormParticipants", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "FormAccessChallenges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GrantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IntakeLinkId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Email = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    CodeHash = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    IssuedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    BlockedUntilUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ConsumedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormAccessChallenges", x => x.Id);
                    table.CheckConstraint(
                        "CK_FormAccessChallenges_Source",
                        "([GrantId] IS NOT NULL AND [IntakeLinkId] IS NULL) OR ([GrantId] IS NULL AND [IntakeLinkId] IS NOT NULL)"
                    );
                    table.ForeignKey(
                        name: "FK_FormAccessChallenges_FormAccessGrants_GrantId",
                        column: x => x.GrantId,
                        principalTable: "FormAccessGrants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                    table.ForeignKey(
                        name: "FK_FormAccessChallenges_FormIntakeLinks_IntakeLinkId",
                        column: x => x.IntakeLinkId,
                        principalTable: "FormIntakeLinks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "FormAccessSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FormRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParticipantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SessionTokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastSeenAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ClientIp = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    UserAgent = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormAccessSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FormAccessSessions_FormParticipants_ParticipantId",
                        column: x => x.ParticipantId,
                        principalTable: "FormParticipants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                    table.ForeignKey(
                        name: "FK_FormAccessSessions_FormRequests_FormRequestId",
                        column: x => x.FormRequestId,
                        principalTable: "FormRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_FormAccessChallenges_GrantId",
                table: "FormAccessChallenges",
                column: "GrantId",
                filter: "[GrantId] IS NOT NULL"
            );

            migrationBuilder.CreateIndex(
                name: "IX_FormAccessChallenges_IntakeLinkId",
                table: "FormAccessChallenges",
                column: "IntakeLinkId",
                filter: "[IntakeLinkId] IS NOT NULL"
            );

            migrationBuilder.CreateIndex(
                name: "IX_FormAccessChallenges_Tenant_Email_Issued",
                table: "FormAccessChallenges",
                columns: new[] { "TenantId", "Email", "IssuedAtUtc" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_FormAccessGrants_FormRequestId",
                table: "FormAccessGrants",
                column: "FormRequestId"
            );

            migrationBuilder.CreateIndex(
                name: "UX_FormAccessGrants_Tenant_Request",
                table: "FormAccessGrants",
                columns: new[] { "TenantId", "FormRequestId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "UX_FormAccessGrants_TokenHash",
                table: "FormAccessGrants",
                column: "TokenHash",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_FormAccessSessions_FormRequestId",
                table: "FormAccessSessions",
                column: "FormRequestId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_FormAccessSessions_ParticipantId",
                table: "FormAccessSessions",
                column: "ParticipantId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_FormAccessSessions_Tenant_Request_Expires",
                table: "FormAccessSessions",
                columns: new[] { "TenantId", "FormRequestId", "ExpiresAtUtc" }
            );

            migrationBuilder.CreateIndex(
                name: "UX_FormAccessSessions_TokenHash",
                table: "FormAccessSessions",
                column: "SessionTokenHash",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_FormIntakeLinks_FormTemplateId",
                table: "FormIntakeLinks",
                column: "FormTemplateId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_FormIntakeLinks_FormTemplateVersionId",
                table: "FormIntakeLinks",
                column: "FormTemplateVersionId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_FormIntakeLinks_Tenant_Status",
                table: "FormIntakeLinks",
                columns: new[] { "TenantId", "Status" }
            );

            migrationBuilder.CreateIndex(
                name: "UX_FormIntakeLinks_TokenHash",
                table: "FormIntakeLinks",
                column: "TokenHash",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_FormParticipants_Tenant_LinkedCustomer",
                table: "FormParticipants",
                columns: new[] { "TenantId", "LinkedCustomerId" },
                filter: "[LinkedCustomerId] IS NOT NULL"
            );

            migrationBuilder.CreateIndex(
                name: "UX_FormParticipants_Tenant_Email_Unlinked",
                table: "FormParticipants",
                columns: new[] { "TenantId", "Email" },
                unique: true,
                filter: "[LinkedCustomerId] IS NULL"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "FormAccessChallenges");

            migrationBuilder.DropTable(name: "FormAccessSessions");

            migrationBuilder.DropTable(name: "FormAccessGrants");

            migrationBuilder.DropTable(name: "FormIntakeLinks");

            migrationBuilder.DropTable(name: "FormParticipants");
        }
    }
}
