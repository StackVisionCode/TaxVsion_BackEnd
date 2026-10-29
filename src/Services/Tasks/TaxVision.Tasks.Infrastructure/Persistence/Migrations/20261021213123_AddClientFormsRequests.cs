using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Tasks.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddClientFormsRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FormFileScanVerdicts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    OwnerType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    OwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FolderType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    ContentType = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    OriginalName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    LastEventOccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormFileScanVerdicts", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "FormRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParticipantKind = table.Column<int>(type: "int", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ParticipantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CustomerLinkStatus = table.Column<int>(type: "int", nullable: false),
                    LinkBlockedCustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LinkBlockedReason = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    LinkConflictingRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Origin = table.Column<int>(type: "int", nullable: false),
                    IntakeLinkId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ResponsibleUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FormTemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FormTemplateVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionNumber = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    MessageToClient = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    TaxYear = table.Column<int>(type: "int", nullable: true),
                    TaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DueAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RequestedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    FirstOpenedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastSubmittedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CurrentRevisionNumber = table.Column<int>(type: "int", nullable: false),
                    DraftProgressPercent = table.Column<int>(type: "int", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompletionNote = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CancelReason = table.Column<int>(type: "int", nullable: true),
                    CancelNote = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ReminderCount = table.Column<int>(type: "int", nullable: false),
                    LastReminderAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PrefillSourceRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ProfileUpdateStatus = table.Column<int>(type: "int", nullable: false),
                    LastActivityAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormRequests", x => x.Id);
                    table.CheckConstraint(
                        "CK_FormRequests_CustomerLink",
                        "([ParticipantKind] = 1 AND [CustomerLinkStatus] = 0 AND [LinkBlockedCustomerId] IS NULL) OR ([ParticipantKind] = 2 AND [CustomerLinkStatus] = 0 AND [CustomerId] IS NULL AND [LinkBlockedCustomerId] IS NULL) OR ([ParticipantKind] = 2 AND [CustomerLinkStatus] = 1 AND [CustomerId] IS NOT NULL AND [LinkBlockedCustomerId] IS NULL) OR ([ParticipantKind] = 2 AND [CustomerLinkStatus] = 2 AND [CustomerId] IS NULL AND [LinkBlockedCustomerId] IS NOT NULL)"
                    );
                    table.CheckConstraint(
                        "CK_FormRequests_Participant",
                        "([ParticipantKind] = 1 AND [CustomerId] IS NOT NULL AND [ParticipantId] IS NULL) OR ([ParticipantKind] = 2 AND [ParticipantId] IS NOT NULL AND [ResponsibleUserId] IS NOT NULL)"
                    );
                    table.ForeignKey(
                        name: "FK_FormRequests_FormTemplateVersions_FormTemplateVersionId",
                        column: x => x.FormTemplateVersionId,
                        principalTable: "FormTemplateVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "FormSubmissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FormRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DraftAnswersCipher = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DraftAnswersKeyVersion = table.Column<short>(type: "smallint", nullable: true),
                    DraftUpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DraftUpdatedByKind = table.Column<int>(type: "int", nullable: true),
                    DraftUpdatedByPortalUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DraftUpdatedByParticipantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DraftUpdatedBySessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormSubmissions", x => x.Id);
                    table.CheckConstraint(
                        "CK_FormSubmissions_DraftUpdatedBy",
                        "[DraftUpdatedByKind] IS NULL OR ([DraftUpdatedByKind] = 1 AND [DraftUpdatedByPortalUserId] IS NOT NULL AND [DraftUpdatedByParticipantId] IS NULL AND [DraftUpdatedBySessionId] IS NULL) OR ([DraftUpdatedByKind] = 2 AND [DraftUpdatedByPortalUserId] IS NULL AND [DraftUpdatedByParticipantId] IS NOT NULL AND [DraftUpdatedBySessionId] IS NOT NULL)"
                    );
                    table.ForeignKey(
                        name: "FK_FormSubmissions_FormRequests_FormRequestId",
                        column: x => x.FormRequestId,
                        principalTable: "FormRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "FormAttachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FormSubmissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ComponentId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    RowId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    FileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RejectionCode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    RegisteredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ResolvedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DetachedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormAttachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FormAttachments_FormSubmissions_FormSubmissionId",
                        column: x => x.FormSubmissionId,
                        principalTable: "FormSubmissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "FormSensitiveAnswers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FormSubmissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ComponentId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    RowId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Cipher = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                    KeyVersion = table.Column<short>(type: "smallint", nullable: false),
                    Last4 = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: false),
                    CapturedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CapturedByKind = table.Column<int>(type: "int", nullable: false),
                    CapturedByPortalUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CapturedByParticipantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CapturedBySessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SupersededAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormSensitiveAnswers", x => x.Id);
                    table.CheckConstraint(
                        "CK_FormSensitiveAnswers_CapturedBy",
                        "([CapturedByKind] = 1 AND [CapturedByPortalUserId] IS NOT NULL AND [CapturedByParticipantId] IS NULL AND [CapturedBySessionId] IS NULL) OR ([CapturedByKind] = 2 AND [CapturedByPortalUserId] IS NULL AND [CapturedByParticipantId] IS NOT NULL AND [CapturedBySessionId] IS NOT NULL)"
                    );
                    table.ForeignKey(
                        name: "FK_FormSensitiveAnswers_FormSubmissions_FormSubmissionId",
                        column: x => x.FormSubmissionId,
                        principalTable: "FormSubmissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "FormSubmissionRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FormSubmissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionNumber = table.Column<int>(type: "int", nullable: false),
                    AnswersCipher = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AnswersKeyVersion = table.Column<short>(type: "smallint", nullable: false),
                    SubmittedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SubmittedByKind = table.Column<int>(type: "int", nullable: false),
                    SubmittedByPortalUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SubmittedByParticipantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SubmittedBySessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Outcome = table.Column<int>(type: "int", nullable: false),
                    ReviewMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ReviewedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReviewedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompletionNote = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    PdfStatus = table.Column<int>(type: "int", nullable: false),
                    PdfGenerationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PdfFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormSubmissionRevisions", x => x.Id);
                    table.CheckConstraint(
                        "CK_FormSubmissionRevisions_SubmittedBy",
                        "([SubmittedByKind] = 1 AND [SubmittedByPortalUserId] IS NOT NULL AND [SubmittedByParticipantId] IS NULL AND [SubmittedBySessionId] IS NULL) OR ([SubmittedByKind] = 2 AND [SubmittedByPortalUserId] IS NULL AND [SubmittedByParticipantId] IS NOT NULL AND [SubmittedBySessionId] IS NOT NULL)"
                    );
                    table.ForeignKey(
                        name: "FK_FormSubmissionRevisions_FormSubmissions_FormSubmissionId",
                        column: x => x.FormSubmissionId,
                        principalTable: "FormSubmissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "FormFieldComments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ComponentId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    RowId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    Comment = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormFieldComments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FormFieldComments_FormSubmissionRevisions_RevisionId",
                        column: x => x.RevisionId,
                        principalTable: "FormSubmissionRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(name: "IX_FormAttachments_FileId", table: "FormAttachments", column: "FileId");

            migrationBuilder.CreateIndex(
                name: "IX_FormAttachments_FormSubmissionId",
                table: "FormAttachments",
                column: "FormSubmissionId"
            );

            migrationBuilder.CreateIndex(
                name: "UX_FormAttachments_Tenant_File_Active",
                table: "FormAttachments",
                columns: new[] { "TenantId", "FileId" },
                unique: true,
                filter: "[Status] <> 4"
            );

            migrationBuilder.CreateIndex(
                name: "IX_FormFieldComments_RevisionId",
                table: "FormFieldComments",
                column: "RevisionId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_FormFileScanVerdicts_TenantId",
                table: "FormFileScanVerdicts",
                column: "TenantId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_FormRequests_FormTemplateVersionId",
                table: "FormRequests",
                column: "FormTemplateVersionId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_FormRequests_TenantId_CustomerId_Status",
                table: "FormRequests",
                columns: new[] { "TenantId", "CustomerId", "Status" },
                filter: "[CustomerId] IS NOT NULL"
            );

            migrationBuilder.CreateIndex(
                name: "IX_FormRequests_TenantId_FormTemplateId",
                table: "FormRequests",
                columns: new[] { "TenantId", "FormTemplateId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_FormRequests_TenantId_ParticipantId_Status",
                table: "FormRequests",
                columns: new[] { "TenantId", "ParticipantId", "Status" },
                filter: "[ParticipantId] IS NOT NULL"
            );

            migrationBuilder.CreateIndex(
                name: "IX_FormRequests_TenantId_ResponsibleUserId_Status",
                table: "FormRequests",
                columns: new[] { "TenantId", "ResponsibleUserId", "Status" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_FormRequests_TenantId_Status_DueAtUtc",
                table: "FormRequests",
                columns: new[] { "TenantId", "Status", "DueAtUtc" }
            );

            migrationBuilder.CreateIndex(
                name: "UX_FormRequests_OpenPerCustomerTemplate",
                table: "FormRequests",
                columns: new[] { "TenantId", "CustomerId", "FormTemplateId" },
                unique: true,
                filter: "[Status] IN (1, 2, 3) AND [CustomerId] IS NOT NULL"
            );

            migrationBuilder.CreateIndex(
                name: "UX_FormRequests_OpenPerParticipantTemplate",
                table: "FormRequests",
                columns: new[] { "TenantId", "ParticipantId", "FormTemplateId" },
                unique: true,
                filter: "[Status] IN (1, 2, 3) AND [CustomerId] IS NULL AND [ParticipantId] IS NOT NULL"
            );

            migrationBuilder.CreateIndex(
                name: "UX_FormSensitiveAnswers_Active",
                table: "FormSensitiveAnswers",
                columns: new[] { "FormSubmissionId", "ComponentId", "RowId" },
                unique: true,
                filter: "[SupersededAtUtc] IS NULL"
            );

            migrationBuilder.CreateIndex(
                name: "IX_FormSubmissionRevisions_PdfGenerationId",
                table: "FormSubmissionRevisions",
                column: "PdfGenerationId"
            );

            migrationBuilder.CreateIndex(
                name: "UX_FormSubmissionRevisions_SubmissionId_Number",
                table: "FormSubmissionRevisions",
                columns: new[] { "FormSubmissionId", "RevisionNumber" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "UX_FormSubmissions_FormRequestId",
                table: "FormSubmissions",
                column: "FormRequestId",
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "FormAttachments");

            migrationBuilder.DropTable(name: "FormFieldComments");

            migrationBuilder.DropTable(name: "FormFileScanVerdicts");

            migrationBuilder.DropTable(name: "FormSensitiveAnswers");

            migrationBuilder.DropTable(name: "FormSubmissionRevisions");

            migrationBuilder.DropTable(name: "FormSubmissions");

            migrationBuilder.DropTable(name: "FormRequests");
        }
    }
}
