using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Tasks.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ClientFormsReviewFixes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Los códigos OTP vivos no se pueden cifrar desde SQL: duran 10 minutos y nada los referencia.
            migrationBuilder.Sql(
                """
                DELETE FROM [FormAccessChallenges];
                """
            );

            migrationBuilder.DropCheckConstraint(name: "CK_FormSubmissions_DraftUpdatedBy", table: "FormSubmissions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_FormSubmissionRevisions_SubmittedBy",
                table: "FormSubmissionRevisions"
            );

            migrationBuilder.DropCheckConstraint(
                name: "CK_FormSensitiveAnswers_CapturedBy",
                table: "FormSensitiveAnswers"
            );

            migrationBuilder.DropIndex(name: "UX_FormConsentRecords_Evidence", table: "FormConsentRecords");

            migrationBuilder.DropCheckConstraint(name: "CK_FormConsentRecords_AcceptedBy", table: "FormConsentRecords");

            migrationBuilder.DropIndex(
                name: "IX_FormAccessChallenges_Tenant_Email_Issued",
                table: "FormAccessChallenges"
            );

            migrationBuilder.DropColumn(name: "Email", table: "FormAccessChallenges");

            migrationBuilder.DropColumn(name: "EntryNoticeLanguage", table: "FormAccessChallenges");

            migrationBuilder.DropColumn(name: "EntryNoticeTextSnapshot", table: "FormAccessChallenges");

            migrationBuilder.AlterColumn<string>(
                name: "DraftUpdatedByKind",
                table: "FormSubmissions",
                type: "nvarchar(24)",
                maxLength: 24,
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<string>(
                name: "SubmittedByKind",
                table: "FormSubmissionRevisions",
                type: "nvarchar(24)",
                maxLength: 24,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int"
            );

            migrationBuilder.AlterColumn<string>(
                name: "CapturedByKind",
                table: "FormSensitiveAnswers",
                type: "nvarchar(24)",
                maxLength: 24,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int"
            );

            migrationBuilder.AlterColumn<string>(
                name: "AcceptedByKind",
                table: "FormConsentRecords",
                type: "nvarchar(24)",
                maxLength: 24,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int"
            );

            // El tipo de actor pasa de entero a texto.
            migrationBuilder.Sql(
                """
                UPDATE [FormSubmissions] SET [DraftUpdatedByKind] = CASE [DraftUpdatedByKind] WHEN N'1' THEN N'PortalUser' WHEN N'2' THEN N'ExternalSession' ELSE [DraftUpdatedByKind] END
                WHERE [DraftUpdatedByKind] IS NOT NULL;
                """
            );

            migrationBuilder.Sql(
                """
                UPDATE [FormSubmissionRevisions] SET [SubmittedByKind] = CASE [SubmittedByKind] WHEN N'1' THEN N'PortalUser' WHEN N'2' THEN N'ExternalSession' ELSE [SubmittedByKind] END
                WHERE [SubmittedByKind] IS NOT NULL;
                """
            );

            migrationBuilder.Sql(
                """
                UPDATE [FormSensitiveAnswers] SET [CapturedByKind] = CASE [CapturedByKind] WHEN N'1' THEN N'PortalUser' WHEN N'2' THEN N'ExternalSession' ELSE [CapturedByKind] END
                WHERE [CapturedByKind] IS NOT NULL;
                """
            );

            migrationBuilder.Sql(
                """
                UPDATE [FormConsentRecords] SET [AcceptedByKind] = CASE [AcceptedByKind] WHEN N'1' THEN N'PortalUser' WHEN N'2' THEN N'ExternalSession' ELSE [AcceptedByKind] END
                WHERE [AcceptedByKind] IS NOT NULL;
                """
            );

            migrationBuilder.AddColumn<string>(
                name: "NoticeBodyJson",
                table: "FormConsentRecords",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: ""
            );

            // La evidencia guarda el cuerpo canónico que firma su hash.
            migrationBuilder.Sql(
                """
                UPDATE r SET r.[NoticeBodyJson] = v.[BodyJson]
                FROM [FormConsentRecords] AS r
                INNER JOIN [ConsentNoticeVersions] AS v
                    ON v.[TenantId] = r.[TenantId] AND v.[ConsentNoticeId] = r.[ConsentNoticeId] AND v.[VersionNumber] = r.[NoticeVersionNumber];
                """
            );

            migrationBuilder.AddColumn<string>(
                name: "EmailCipher",
                table: "FormAccessChallenges",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: false,
                defaultValue: ""
            );

            migrationBuilder.AddColumn<short>(
                name: "EmailKeyVersion",
                table: "FormAccessChallenges",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0
            );

            migrationBuilder.AddColumn<string>(
                name: "EmailLookupHash",
                table: "FormAccessChallenges",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: ""
            );

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "FormAccessChallenges",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_FormSubmissions_DraftUpdatedBy",
                table: "FormSubmissions",
                sql: "[DraftUpdatedByKind] IS NULL OR ([DraftUpdatedByKind] = N'PortalUser' AND [DraftUpdatedByPortalUserId] IS NOT NULL AND [DraftUpdatedByParticipantId] IS NULL AND [DraftUpdatedBySessionId] IS NULL) OR ([DraftUpdatedByKind] = N'ExternalSession' AND [DraftUpdatedByPortalUserId] IS NULL AND [DraftUpdatedByParticipantId] IS NOT NULL AND [DraftUpdatedBySessionId] IS NOT NULL)"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_FormSubmissionRevisions_SubmittedBy",
                table: "FormSubmissionRevisions",
                sql: "([SubmittedByKind] = N'PortalUser' AND [SubmittedByPortalUserId] IS NOT NULL AND [SubmittedByParticipantId] IS NULL AND [SubmittedBySessionId] IS NULL) OR ([SubmittedByKind] = N'ExternalSession' AND [SubmittedByPortalUserId] IS NULL AND [SubmittedByParticipantId] IS NOT NULL AND [SubmittedBySessionId] IS NOT NULL)"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_FormSensitiveAnswers_CapturedBy",
                table: "FormSensitiveAnswers",
                sql: "([CapturedByKind] = N'PortalUser' AND [CapturedByPortalUserId] IS NOT NULL AND [CapturedByParticipantId] IS NULL AND [CapturedBySessionId] IS NULL) OR ([CapturedByKind] = N'ExternalSession' AND [CapturedByPortalUserId] IS NULL AND [CapturedByParticipantId] IS NOT NULL AND [CapturedBySessionId] IS NOT NULL)"
            );

            migrationBuilder.CreateIndex(
                name: "UX_FormConsentRecords_Evidence",
                table: "FormConsentRecords",
                columns: new[]
                {
                    "TenantId",
                    "FormRequestId",
                    "ComponentId",
                    "ParticipantKind",
                    "CustomerId",
                    "ParticipantId",
                    "ConsentNoticeId",
                    "NoticeVersionNumber",
                    "Outcome",
                },
                unique: true
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_FormConsentRecords_AcceptedBy",
                table: "FormConsentRecords",
                sql: "([AcceptedByKind] = N'PortalUser' AND [AcceptedByPortalUserId] IS NOT NULL AND [AcceptedByParticipantId] IS NULL AND [AcceptedBySessionId] IS NULL) OR ([AcceptedByKind] = N'ExternalSession' AND [AcceptedByPortalUserId] IS NULL AND [AcceptedByParticipantId] IS NOT NULL AND [AcceptedBySessionId] IS NOT NULL)"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_FormConsentRecords_NoticeBody_IsJson",
                table: "FormConsentRecords",
                sql: "ISJSON([NoticeBodyJson]) = 1"
            );

            migrationBuilder.CreateIndex(
                name: "IX_FormAttachments_Status_RegisteredAtUtc",
                table: "FormAttachments",
                columns: new[] { "Status", "RegisteredAtUtc" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_FormAccessChallenges_Tenant_EmailLookup_Issued",
                table: "FormAccessChallenges",
                columns: new[] { "TenantId", "EmailLookupHash", "IssuedAtUtc" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DELETE FROM [FormAccessChallenges];
                """
            );

            migrationBuilder.DropCheckConstraint(name: "CK_FormSubmissions_DraftUpdatedBy", table: "FormSubmissions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_FormSubmissionRevisions_SubmittedBy",
                table: "FormSubmissionRevisions"
            );

            migrationBuilder.DropCheckConstraint(
                name: "CK_FormSensitiveAnswers_CapturedBy",
                table: "FormSensitiveAnswers"
            );

            migrationBuilder.DropIndex(name: "UX_FormConsentRecords_Evidence", table: "FormConsentRecords");

            migrationBuilder.DropCheckConstraint(name: "CK_FormConsentRecords_AcceptedBy", table: "FormConsentRecords");

            migrationBuilder.DropCheckConstraint(
                name: "CK_FormConsentRecords_NoticeBody_IsJson",
                table: "FormConsentRecords"
            );

            migrationBuilder.DropIndex(name: "IX_FormAttachments_Status_RegisteredAtUtc", table: "FormAttachments");

            migrationBuilder.DropIndex(
                name: "IX_FormAccessChallenges_Tenant_EmailLookup_Issued",
                table: "FormAccessChallenges"
            );

            migrationBuilder.DropColumn(name: "NoticeBodyJson", table: "FormConsentRecords");

            migrationBuilder.DropColumn(name: "EmailCipher", table: "FormAccessChallenges");

            migrationBuilder.DropColumn(name: "EmailKeyVersion", table: "FormAccessChallenges");

            migrationBuilder.DropColumn(name: "EmailLookupHash", table: "FormAccessChallenges");

            migrationBuilder.DropColumn(name: "RowVersion", table: "FormAccessChallenges");

            migrationBuilder.Sql(
                """
                UPDATE [FormSubmissions] SET [DraftUpdatedByKind] = CASE [DraftUpdatedByKind] WHEN N'PortalUser' THEN N'1' WHEN N'ExternalSession' THEN N'2' ELSE [DraftUpdatedByKind] END
                WHERE [DraftUpdatedByKind] IS NOT NULL;
                """
            );

            migrationBuilder.Sql(
                """
                UPDATE [FormSubmissionRevisions] SET [SubmittedByKind] = CASE [SubmittedByKind] WHEN N'PortalUser' THEN N'1' WHEN N'ExternalSession' THEN N'2' ELSE [SubmittedByKind] END
                WHERE [SubmittedByKind] IS NOT NULL;
                """
            );

            migrationBuilder.Sql(
                """
                UPDATE [FormSensitiveAnswers] SET [CapturedByKind] = CASE [CapturedByKind] WHEN N'PortalUser' THEN N'1' WHEN N'ExternalSession' THEN N'2' ELSE [CapturedByKind] END
                WHERE [CapturedByKind] IS NOT NULL;
                """
            );

            migrationBuilder.Sql(
                """
                UPDATE [FormConsentRecords] SET [AcceptedByKind] = CASE [AcceptedByKind] WHEN N'PortalUser' THEN N'1' WHEN N'ExternalSession' THEN N'2' ELSE [AcceptedByKind] END
                WHERE [AcceptedByKind] IS NOT NULL;
                """
            );

            migrationBuilder.AlterColumn<int>(
                name: "DraftUpdatedByKind",
                table: "FormSubmissions",
                type: "int",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(24)",
                oldMaxLength: 24,
                oldNullable: true
            );

            migrationBuilder.AlterColumn<int>(
                name: "SubmittedByKind",
                table: "FormSubmissionRevisions",
                type: "int",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(24)",
                oldMaxLength: 24
            );

            migrationBuilder.AlterColumn<int>(
                name: "CapturedByKind",
                table: "FormSensitiveAnswers",
                type: "int",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(24)",
                oldMaxLength: 24
            );

            migrationBuilder.AlterColumn<int>(
                name: "AcceptedByKind",
                table: "FormConsentRecords",
                type: "int",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(24)",
                oldMaxLength: 24
            );

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "FormAccessChallenges",
                type: "nvarchar(254)",
                maxLength: 254,
                nullable: false,
                defaultValue: ""
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

            migrationBuilder.AddCheckConstraint(
                name: "CK_FormSubmissions_DraftUpdatedBy",
                table: "FormSubmissions",
                sql: "[DraftUpdatedByKind] IS NULL OR ([DraftUpdatedByKind] = 1 AND [DraftUpdatedByPortalUserId] IS NOT NULL AND [DraftUpdatedByParticipantId] IS NULL AND [DraftUpdatedBySessionId] IS NULL) OR ([DraftUpdatedByKind] = 2 AND [DraftUpdatedByPortalUserId] IS NULL AND [DraftUpdatedByParticipantId] IS NOT NULL AND [DraftUpdatedBySessionId] IS NOT NULL)"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_FormSubmissionRevisions_SubmittedBy",
                table: "FormSubmissionRevisions",
                sql: "([SubmittedByKind] = 1 AND [SubmittedByPortalUserId] IS NOT NULL AND [SubmittedByParticipantId] IS NULL AND [SubmittedBySessionId] IS NULL) OR ([SubmittedByKind] = 2 AND [SubmittedByPortalUserId] IS NULL AND [SubmittedByParticipantId] IS NOT NULL AND [SubmittedBySessionId] IS NOT NULL)"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_FormSensitiveAnswers_CapturedBy",
                table: "FormSensitiveAnswers",
                sql: "([CapturedByKind] = 1 AND [CapturedByPortalUserId] IS NOT NULL AND [CapturedByParticipantId] IS NULL AND [CapturedBySessionId] IS NULL) OR ([CapturedByKind] = 2 AND [CapturedByPortalUserId] IS NULL AND [CapturedByParticipantId] IS NOT NULL AND [CapturedBySessionId] IS NOT NULL)"
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

            migrationBuilder.AddCheckConstraint(
                name: "CK_FormConsentRecords_AcceptedBy",
                table: "FormConsentRecords",
                sql: "([AcceptedByKind] = 1 AND [AcceptedByPortalUserId] IS NOT NULL AND [AcceptedByParticipantId] IS NULL AND [AcceptedBySessionId] IS NULL) OR ([AcceptedByKind] = 2 AND [AcceptedByPortalUserId] IS NULL AND [AcceptedByParticipantId] IS NOT NULL AND [AcceptedBySessionId] IS NOT NULL)"
            );

            migrationBuilder.CreateIndex(
                name: "IX_FormAccessChallenges_Tenant_Email_Issued",
                table: "FormAccessChallenges",
                columns: new[] { "TenantId", "Email", "IssuedAtUtc" }
            );
        }
    }
}
