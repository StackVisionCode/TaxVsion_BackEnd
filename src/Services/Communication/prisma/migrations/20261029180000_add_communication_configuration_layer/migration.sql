BEGIN TRY

BEGIN TRAN;

-- AlterTable: ajustes de oficina dentro del techo efectivo (null = hereda).
ALTER TABLE [dbo].[TenantCommunicationSettings] ADD
    [DefaultMeetingDurationMinutes] INT,
    [MaxMeetingDurationMinutes] INT,
    [MaxMeetingParticipants] INT,
    [MaxRecordingMinutesMeeting] INT,
    [MaxRecordingMinutesAudioCall] INT,
    [MaxRecordingMinutesVideoCall] INT,
    [RingingTimeoutSeconds] INT,
    [MeetingChatAttachmentsPerMinute] INT,
    [MeetingChatAttachmentsPerMeeting] INT,
    [MeetingSoundsPolicy] NVARCHAR(16),
    [TranscriptLanguage] NVARCHAR(16),
    [RecordingVisibleInPortal] BIT NOT NULL CONSTRAINT [TenantCommunicationSettings_RecordingVisibleInPortal_df] DEFAULT 1,
    [GuestsAllowed] BIT NOT NULL CONSTRAINT [TenantCommunicationSettings_GuestsAllowed_df] DEFAULT 1;

-- AlterTable: los valores del plan dejan de tener default en código/BD (null = clave no definida).
ALTER TABLE [dbo].[TenantCommunicationLimits] DROP CONSTRAINT [TenantCommunicationLimits_MaxMeetingParticipants_df];
ALTER TABLE [dbo].[TenantCommunicationLimits] DROP CONSTRAINT [TenantCommunicationLimits_MaxMeetingMinutes_df];
ALTER TABLE [dbo].[TenantCommunicationLimits] DROP CONSTRAINT [TenantCommunicationLimits_MaxConcurrentCalls_df];
ALTER TABLE [dbo].[TenantCommunicationLimits] DROP CONSTRAINT [TenantCommunicationLimits_MaxMonthlyMinutes_df];
ALTER TABLE [dbo].[TenantCommunicationLimits] DROP CONSTRAINT [TenantCommunicationLimits_RecordingEnabled_df];
ALTER TABLE [dbo].[TenantCommunicationLimits] ALTER COLUMN [MaxMeetingParticipants] INT NULL;
ALTER TABLE [dbo].[TenantCommunicationLimits] ALTER COLUMN [MaxMeetingMinutes] INT NULL;
ALTER TABLE [dbo].[TenantCommunicationLimits] ALTER COLUMN [MaxConcurrentCalls] INT NULL;
ALTER TABLE [dbo].[TenantCommunicationLimits] ALTER COLUMN [MaxMonthlyMinutes] INT NULL;
ALTER TABLE [dbo].[TenantCommunicationLimits] ALTER COLUMN [RecordingEnabled] BIT NULL;
ALTER TABLE [dbo].[TenantCommunicationLimits] ADD
    [MaxRecordingMinutesMeeting] INT,
    [MaxRecordingMinutesAudioCall] INT,
    [MaxRecordingMinutesVideoCall] INT,
    [RecordingVideoEnabled] BIT,
    [TranscriptEnabled] BIT,
    [CallsEnabled] BIT,
    [MaxCallMinutes] INT;

-- CreateTable
CREATE TABLE [dbo].[PlatformCommunicationSettings] (
    [Id] NVARCHAR(16) NOT NULL CONSTRAINT [PlatformCommunicationSettings_Id_df] DEFAULT 'global',
    [Version] INT NOT NULL CONSTRAINT [PlatformCommunicationSettings_Version_df] DEFAULT 1,
    [MaxMeetingDurationMinutes] INT NOT NULL CONSTRAINT [PlatformCommunicationSettings_MaxMeetingDurationMinutes_df] DEFAULT 60,
    [DefaultMeetingDurationMinutes] INT NOT NULL CONSTRAINT [PlatformCommunicationSettings_DefaultMeetingDurationMinutes_df] DEFAULT 60,
    [DefaultMaxParticipants] INT NOT NULL CONSTRAINT [PlatformCommunicationSettings_DefaultMaxParticipants_df] DEFAULT 10,
    [MeshMaxParticipants] INT NOT NULL CONSTRAINT [PlatformCommunicationSettings_MeshMaxParticipants_df] DEFAULT 4,
    [DisconnectGraceSeconds] INT NOT NULL CONSTRAINT [PlatformCommunicationSettings_DisconnectGraceSeconds_df] DEFAULT 30,
    [ScheduledMeetingExpiryHours] INT NOT NULL CONSTRAINT [PlatformCommunicationSettings_ScheduledMeetingExpiryHours_df] DEFAULT 24,
    [EmptyMeetingEndMinutes] INT NOT NULL CONSTRAINT [PlatformCommunicationSettings_EmptyMeetingEndMinutes_df] DEFAULT 10,
    [DominantSpeakerMaxPerSecond] INT NOT NULL CONSTRAINT [PlatformCommunicationSettings_DominantSpeakerMaxPerSecond_df] DEFAULT 5,
    [GuestsAllowed] BIT NOT NULL CONSTRAINT [PlatformCommunicationSettings_GuestsAllowed_df] DEFAULT 1,
    [RingingTimeoutSeconds] INT NOT NULL CONSTRAINT [PlatformCommunicationSettings_RingingTimeoutSeconds_df] DEFAULT 60,
    [RecordingConsentTimeoutMeetingSeconds] INT NOT NULL CONSTRAINT [PlatformCommunicationSettings_RecordingConsentTimeoutMeetingSeconds_df] DEFAULT 30,
    [RecordingConsentTimeoutCallSeconds] INT NOT NULL CONSTRAINT [PlatformCommunicationSettings_RecordingConsentTimeoutCallSeconds_df] DEFAULT 15,
    [MaxRecordingMinutesMeeting] INT NOT NULL CONSTRAINT [PlatformCommunicationSettings_MaxRecordingMinutesMeeting_df] DEFAULT 60,
    [MaxRecordingMinutesAudioCall] INT NOT NULL CONSTRAINT [PlatformCommunicationSettings_MaxRecordingMinutesAudioCall_df] DEFAULT 60,
    [MaxRecordingMinutesVideoCall] INT NOT NULL CONSTRAINT [PlatformCommunicationSettings_MaxRecordingMinutesVideoCall_df] DEFAULT 60,
    [InvitationMinTtlHours] INT NOT NULL CONSTRAINT [PlatformCommunicationSettings_InvitationMinTtlHours_df] DEFAULT 168,
    [InvitationExpiryGraceHours] INT NOT NULL CONSTRAINT [PlatformCommunicationSettings_InvitationExpiryGraceHours_df] DEFAULT 24,
    [InternalGroupsEnabled] BIT NOT NULL CONSTRAINT [PlatformCommunicationSettings_InternalGroupsEnabled_df] DEFAULT 1,
    [MeetingChatAttachmentsPerMinute] INT NOT NULL CONSTRAINT [PlatformCommunicationSettings_MeetingChatAttachmentsPerMinute_df] DEFAULT 5,
    [MeetingChatAttachmentsPerMeeting] INT NOT NULL CONSTRAINT [PlatformCommunicationSettings_MeetingChatAttachmentsPerMeeting_df] DEFAULT 20,
    [MaxReactionsPerMessage] INT NOT NULL CONSTRAINT [PlatformCommunicationSettings_MaxReactionsPerMessage_df] DEFAULT 3,
    [MeetingSoundsPolicy] NVARCHAR(16) NOT NULL CONSTRAINT [PlatformCommunicationSettings_MeetingSoundsPolicy_df] DEFAULT 'all',
    [TranscriptDefaultLanguage] NVARCHAR(16) NOT NULL CONSTRAINT [PlatformCommunicationSettings_TranscriptDefaultLanguage_df] DEFAULT 'auto',
    [OutboxRetentionDays] INT NOT NULL CONSTRAINT [PlatformCommunicationSettings_OutboxRetentionDays_df] DEFAULT 7,
    [ProcessedEventRetentionDays] INT NOT NULL CONSTRAINT [PlatformCommunicationSettings_ProcessedEventRetentionDays_df] DEFAULT 14,
    [IdempotencyRetentionDays] INT NOT NULL CONSTRAINT [PlatformCommunicationSettings_IdempotencyRetentionDays_df] DEFAULT 30,
    [UpdatedByUserId] UNIQUEIDENTIFIER,
    [UpdatedAtUtc] DATETIME2 NOT NULL CONSTRAINT [PlatformCommunicationSettings_UpdatedAtUtc_df] DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT [PlatformCommunicationSettings_pkey] PRIMARY KEY CLUSTERED ([Id])
);

-- CreateTable
CREATE TABLE [dbo].[TenantCommunicationConstraints] (
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [Version] INT NOT NULL CONSTRAINT [TenantCommunicationConstraints_Version_df] DEFAULT 1,
    [MaxMeetingParticipants] INT,
    [MaxMeetingDurationMinutes] INT,
    [MaxRecordingMinutesMeeting] INT,
    [MaxRecordingMinutesAudioCall] INT,
    [MaxRecordingMinutesVideoCall] INT,
    [RecordingAllowed] BIT,
    [GuestsAllowed] BIT,
    [Notes] NVARCHAR(512),
    [UpdatedByUserId] UNIQUEIDENTIFIER,
    [CreatedAtUtc] DATETIME2 NOT NULL CONSTRAINT [TenantCommunicationConstraints_CreatedAtUtc_df] DEFAULT CURRENT_TIMESTAMP,
    [UpdatedAtUtc] DATETIME2 NOT NULL CONSTRAINT [TenantCommunicationConstraints_UpdatedAtUtc_df] DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT [TenantCommunicationConstraints_pkey] PRIMARY KEY CLUSTERED ([TenantId])
);

-- CreateTable
CREATE TABLE [dbo].[UserCommunicationPreferences] (
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [MeetingSoundsMuted] BIT NOT NULL CONSTRAINT [UserCommunicationPreferences_MeetingSoundsMuted_df] DEFAULT 0,
    [TranscriptLanguage] NVARCHAR(16),
    [PreferAudioOnly] BIT NOT NULL CONSTRAINT [UserCommunicationPreferences_PreferAudioOnly_df] DEFAULT 0,
    [UpdatedAtUtc] DATETIME2 NOT NULL CONSTRAINT [UserCommunicationPreferences_UpdatedAtUtc_df] DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT [UserCommunicationPreferences_pkey] PRIMARY KEY CLUSTERED ([TenantId],[UserId])
);

-- CreateTable
CREATE TABLE [dbo].[ConfigurationAudit] (
    [Id] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER,
    [Scope] NVARCHAR(32) NOT NULL,
    [Key] NVARCHAR(128) NOT NULL,
    [OldValue] NVARCHAR(512),
    [NewValue] NVARCHAR(512),
    [ActorUserId] UNIQUEIDENTIFIER,
    [Reason] NVARCHAR(32) NOT NULL,
    [CorrelationId] NVARCHAR(128),
    [CreatedAtUtc] DATETIME2 NOT NULL CONSTRAINT [ConfigurationAudit_CreatedAtUtc_df] DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT [ConfigurationAudit_pkey] PRIMARY KEY CLUSTERED ([Id])
);

-- CreateIndex
CREATE NONCLUSTERED INDEX [ConfigurationAudit_TenantId_CreatedAtUtc_idx] ON [dbo].[ConfigurationAudit]([TenantId], [CreatedAtUtc]);

-- CreateIndex
CREATE NONCLUSTERED INDEX [ConfigurationAudit_Scope_CreatedAtUtc_idx] ON [dbo].[ConfigurationAudit]([Scope], [CreatedAtUtc]);

-- Siembra (D-37): la fila global nace con los valores decididos por el dueño (defaults de columna).
INSERT INTO [dbo].[PlatformCommunicationSettings] ([Id]) VALUES ('global');

COMMIT TRAN;

END TRY
BEGIN CATCH

IF @@TRANCOUNT > 0
BEGIN
    ROLLBACK TRAN;
END;
THROW

END CATCH
