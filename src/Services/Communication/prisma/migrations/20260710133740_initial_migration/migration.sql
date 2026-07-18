BEGIN TRY

BEGIN TRAN;

-- CreateTable
CREATE TABLE [dbo].[TenantCommunicationSettings] (
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [ChatEnabled] BIT NOT NULL CONSTRAINT [TenantCommunicationSettings_ChatEnabled_df] DEFAULT 1,
    [CallsEnabled] BIT NOT NULL CONSTRAINT [TenantCommunicationSettings_CallsEnabled_df] DEFAULT 1,
    [VideoCallsEnabled] BIT NOT NULL CONSTRAINT [TenantCommunicationSettings_VideoCallsEnabled_df] DEFAULT 1,
    [MeetingsEnabled] BIT NOT NULL CONSTRAINT [TenantCommunicationSettings_MeetingsEnabled_df] DEFAULT 1,
    [SupportEnabled] BIT NOT NULL CONSTRAINT [TenantCommunicationSettings_SupportEnabled_df] DEFAULT 1,
    [ScreenshotsEnabled] BIT NOT NULL CONSTRAINT [TenantCommunicationSettings_ScreenshotsEnabled_df] DEFAULT 1,
    [InternalGroupsEnabled] BIT NOT NULL CONSTRAINT [TenantCommunicationSettings_InternalGroupsEnabled_df] DEFAULT 0,
    [EmployeeToEmployeeChatEnabled] BIT NOT NULL CONSTRAINT [TenantCommunicationSettings_EmployeeToEmployeeChatEnabled_df] DEFAULT 0,
    [DefaultCameraOff] BIT NOT NULL CONSTRAINT [TenantCommunicationSettings_DefaultCameraOff_df] DEFAULT 1,
    [DefaultMicrophoneOff] BIT NOT NULL CONSTRAINT [TenantCommunicationSettings_DefaultMicrophoneOff_df] DEFAULT 0,
    [PersistChatOnEnd] BIT NOT NULL CONSTRAINT [TenantCommunicationSettings_PersistChatOnEnd_df] DEFAULT 0,
    [MessageRetentionDays] INT NOT NULL CONSTRAINT [TenantCommunicationSettings_MessageRetentionDays_df] DEFAULT 365,
    [RecordingRetentionDays] INT NOT NULL CONSTRAINT [TenantCommunicationSettings_RecordingRetentionDays_df] DEFAULT 90,
    [PurgeEnabled] BIT NOT NULL CONSTRAINT [TenantCommunicationSettings_PurgeEnabled_df] DEFAULT 0,
    [CreatedAtUtc] DATETIME2 NOT NULL CONSTRAINT [TenantCommunicationSettings_CreatedAtUtc_df] DEFAULT CURRENT_TIMESTAMP,
    [UpdatedAtUtc] DATETIME2 NOT NULL CONSTRAINT [TenantCommunicationSettings_UpdatedAtUtc_df] DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT [TenantCommunicationSettings_pkey] PRIMARY KEY CLUSTERED ([TenantId])
);

-- CreateTable
CREATE TABLE [dbo].[TenantCommunicationLimits] (
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [PlanCode] NVARCHAR(64) NOT NULL,
    [MaxMeetingParticipants] INT NOT NULL CONSTRAINT [TenantCommunicationLimits_MaxMeetingParticipants_df] DEFAULT 4,
    [MaxMeetingMinutes] INT NOT NULL CONSTRAINT [TenantCommunicationLimits_MaxMeetingMinutes_df] DEFAULT 60,
    [MaxConcurrentCalls] INT NOT NULL CONSTRAINT [TenantCommunicationLimits_MaxConcurrentCalls_df] DEFAULT 2,
    [MaxMonthlyMinutes] INT NOT NULL CONSTRAINT [TenantCommunicationLimits_MaxMonthlyMinutes_df] DEFAULT 600,
    [RecordingEnabled] BIT NOT NULL CONSTRAINT [TenantCommunicationLimits_RecordingEnabled_df] DEFAULT 0,
    [SupportEnabled] BIT NOT NULL CONSTRAINT [TenantCommunicationLimits_SupportEnabled_df] DEFAULT 1,
    [IsSuspended] BIT NOT NULL CONSTRAINT [TenantCommunicationLimits_IsSuspended_df] DEFAULT 0,
    [UpdatedAtUtc] DATETIME2 NOT NULL CONSTRAINT [TenantCommunicationLimits_UpdatedAtUtc_df] DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT [TenantCommunicationLimits_pkey] PRIMARY KEY CLUSTERED ([TenantId])
);

-- CreateTable
CREATE TABLE [dbo].[UserPermissionsProjection] (
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [Permissions] NVARCHAR(max) NOT NULL,
    [PermVersion] INT NOT NULL CONSTRAINT [UserPermissionsProjection_PermVersion_df] DEFAULT 1,
    [ActorType] NVARCHAR(64) NOT NULL,
    [IsActive] BIT NOT NULL CONSTRAINT [UserPermissionsProjection_IsActive_df] DEFAULT 1,
    [UpdatedAtUtc] DATETIME2 NOT NULL CONSTRAINT [UserPermissionsProjection_UpdatedAtUtc_df] DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT [UserPermissionsProjection_pkey] PRIMARY KEY CLUSTERED ([UserId])
);

-- CreateTable
CREATE TABLE [dbo].[ProcessedEvent] (
    [EventId] UNIQUEIDENTIFIER NOT NULL,
    [Source] NVARCHAR(64) NOT NULL,
    [EventType] NVARCHAR(128) NOT NULL,
    [TenantId] UNIQUEIDENTIFIER,
    [ProcessedAtUtc] DATETIME2 NOT NULL CONSTRAINT [ProcessedEvent_ProcessedAtUtc_df] DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT [ProcessedEvent_pkey] PRIMARY KEY CLUSTERED ([EventId])
);

-- CreateTable
CREATE TABLE [dbo].[OutboxMessage] (
    [Id] UNIQUEIDENTIFIER NOT NULL,
    [EventId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [EventType] NVARCHAR(128) NOT NULL,
    [Payload] NVARCHAR(max) NOT NULL,
    [CorrelationId] NVARCHAR(64),
    [OccurredAtUtc] DATETIME2 NOT NULL,
    [PublishedAtUtc] DATETIME2,
    [Attempts] INT NOT NULL CONSTRAINT [OutboxMessage_Attempts_df] DEFAULT 0,
    [LastError] NVARCHAR(max),
    [CreatedAtUtc] DATETIME2 NOT NULL CONSTRAINT [OutboxMessage_CreatedAtUtc_df] DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT [OutboxMessage_pkey] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [OutboxMessage_EventId_key] UNIQUE NONCLUSTERED ([EventId])
);

-- CreateTable
CREATE TABLE [dbo].[IdempotencyRecord] (
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [Scope] NVARCHAR(64) NOT NULL,
    [ClientKey] NVARCHAR(128) NOT NULL,
    [ResultPayload] NVARCHAR(max) NOT NULL,
    [CreatedAtUtc] DATETIME2 NOT NULL CONSTRAINT [IdempotencyRecord_CreatedAtUtc_df] DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT [IdempotencyRecord_pkey] PRIMARY KEY CLUSTERED ([TenantId],[UserId],[Scope],[ClientKey])
);

-- CreateTable
CREATE TABLE [dbo].[Conversation] (
    [Id] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [Kind] NVARCHAR(32) NOT NULL,
    [Title] NVARCHAR(120),
    [UniquenessKey] NVARCHAR(256) NOT NULL,
    [IsArchived] BIT NOT NULL CONSTRAINT [Conversation_IsArchived_df] DEFAULT 0,
    [LastMessageAtUtc] DATETIME2,
    [CreatedByUserId] UNIQUEIDENTIFIER NOT NULL,
    [CreatedAtUtc] DATETIME2 NOT NULL CONSTRAINT [Conversation_CreatedAtUtc_df] DEFAULT CURRENT_TIMESTAMP,
    [UpdatedAtUtc] DATETIME2 NOT NULL CONSTRAINT [Conversation_UpdatedAtUtc_df] DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT [Conversation_pkey] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [Conversation_TenantId_UniquenessKey_key] UNIQUE NONCLUSTERED ([TenantId],[UniquenessKey])
);

-- CreateTable
CREATE TABLE [dbo].[ConversationParticipant] (
    [Id] UNIQUEIDENTIFIER NOT NULL,
    [ConversationId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [DisplayName] NVARCHAR(120) NOT NULL,
    [ActorType] NVARCHAR(32) NOT NULL,
    [Role] NVARCHAR(32) NOT NULL CONSTRAINT [ConversationParticipant_Role_df] DEFAULT 'Member',
    [IsPrimaryPreparer] BIT NOT NULL CONSTRAINT [ConversationParticipant_IsPrimaryPreparer_df] DEFAULT 0,
    [IsMuted] BIT NOT NULL CONSTRAINT [ConversationParticipant_IsMuted_df] DEFAULT 0,
    [IsRemoved] BIT NOT NULL CONSTRAINT [ConversationParticipant_IsRemoved_df] DEFAULT 0,
    [JoinedAtUtc] DATETIME2 NOT NULL CONSTRAINT [ConversationParticipant_JoinedAtUtc_df] DEFAULT CURRENT_TIMESTAMP,
    [RemovedAtUtc] DATETIME2,
    [LastReadAtUtc] DATETIME2,
    [LastReadMessageId] UNIQUEIDENTIFIER,
    CONSTRAINT [ConversationParticipant_pkey] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [ConversationParticipant_ConversationId_UserId_key] UNIQUE NONCLUSTERED ([ConversationId],[UserId])
);

-- CreateTable
CREATE TABLE [dbo].[Message] (
    [Id] UNIQUEIDENTIFIER NOT NULL,
    [ConversationId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [SenderId] UNIQUEIDENTIFIER NOT NULL,
    [SenderDisplayName] NVARCHAR(120) NOT NULL,
    [Kind] NVARCHAR(32) NOT NULL,
    [Body] NVARCHAR(4000),
    [AttachmentFileId] UNIQUEIDENTIFIER,
    [ReplyToMessageId] UNIQUEIDENTIFIER,
    [IsEdited] BIT NOT NULL CONSTRAINT [Message_IsEdited_df] DEFAULT 0,
    [IsDeleted] BIT NOT NULL CONSTRAINT [Message_IsDeleted_df] DEFAULT 0,
    [DeletedAtUtc] DATETIME2,
    [CreatedAtUtc] DATETIME2 NOT NULL CONSTRAINT [Message_CreatedAtUtc_df] DEFAULT CURRENT_TIMESTAMP,
    [EditedAtUtc] DATETIME2,
    CONSTRAINT [Message_pkey] PRIMARY KEY CLUSTERED ([Id])
);

-- CreateTable
CREATE TABLE [dbo].[MessageReceipt] (
    [MessageId] UNIQUEIDENTIFIER NOT NULL,
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [DeliveredAtUtc] DATETIME2,
    [ReadAtUtc] DATETIME2,
    CONSTRAINT [MessageReceipt_pkey] PRIMARY KEY CLUSTERED ([MessageId],[UserId])
);

-- CreateTable
CREATE TABLE [dbo].[Call] (
    [Id] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [Kind] NVARCHAR(16) NOT NULL,
    [Status] NVARCHAR(24) NOT NULL,
    [CallerUserId] UNIQUEIDENTIFIER NOT NULL,
    [CalleeUserId] UNIQUEIDENTIFIER NOT NULL,
    [ConversationId] UNIQUEIDENTIFIER,
    [RingingAtUtc] DATETIME2 NOT NULL,
    [AcceptedAtUtc] DATETIME2,
    [StartedAtUtc] DATETIME2,
    [EndedAtUtc] DATETIME2,
    [EndReason] NVARCHAR(64),
    [DurationSeconds] INT,
    [RecordingRequested] BIT NOT NULL CONSTRAINT [Call_RecordingRequested_df] DEFAULT 0,
    [RecordingFileId] UNIQUEIDENTIFIER,
    [CreatedAtUtc] DATETIME2 NOT NULL CONSTRAINT [Call_CreatedAtUtc_df] DEFAULT CURRENT_TIMESTAMP,
    [UpdatedAtUtc] DATETIME2 NOT NULL CONSTRAINT [Call_UpdatedAtUtc_df] DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT [Call_pkey] PRIMARY KEY CLUSTERED ([Id])
);

-- CreateTable
CREATE TABLE [dbo].[CallParticipant] (
    [Id] UNIQUEIDENTIFIER NOT NULL,
    [CallId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [DisplayName] NVARCHAR(120) NOT NULL,
    [Role] NVARCHAR(16) NOT NULL,
    [JoinOrder] INT NOT NULL,
    [JoinedAtUtc] DATETIME2 NOT NULL CONSTRAINT [CallParticipant_JoinedAtUtc_df] DEFAULT CURRENT_TIMESTAMP,
    [LeftAtUtc] DATETIME2,
    [AudioEnabled] BIT NOT NULL CONSTRAINT [CallParticipant_AudioEnabled_df] DEFAULT 1,
    [VideoEnabled] BIT NOT NULL CONSTRAINT [CallParticipant_VideoEnabled_df] DEFAULT 1,
    [ScreenSharing] BIT NOT NULL CONSTRAINT [CallParticipant_ScreenSharing_df] DEFAULT 0,
    [ConnectionQuality] NVARCHAR(16) NOT NULL CONSTRAINT [CallParticipant_ConnectionQuality_df] DEFAULT 'Unknown',
    CONSTRAINT [CallParticipant_pkey] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [CallParticipant_CallId_UserId_key] UNIQUE NONCLUSTERED ([CallId],[UserId])
);

-- CreateTable
CREATE TABLE [dbo].[Meeting] (
    [Id] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [Title] NVARCHAR(200) NOT NULL,
    [Description] NVARCHAR(1000),
    [Status] NVARCHAR(24) NOT NULL,
    [ShortCode] NVARCHAR(16) NOT NULL,
    [PasscodeHash] NVARCHAR(255),
    [RequireWaitingRoom] BIT NOT NULL CONSTRAINT [Meeting_RequireWaitingRoom_df] DEFAULT 1,
    [IsLocked] BIT NOT NULL CONSTRAINT [Meeting_IsLocked_df] DEFAULT 0,
    [MaxParticipants] INT NOT NULL CONSTRAINT [Meeting_MaxParticipants_df] DEFAULT 4,
    [Strategy] NVARCHAR(16) NOT NULL CONSTRAINT [Meeting_Strategy_df] DEFAULT 'Mesh',
    [RecordingRequested] BIT NOT NULL CONSTRAINT [Meeting_RecordingRequested_df] DEFAULT 0,
    [RecordingFileId] UNIQUEIDENTIFIER,
    [HostUserId] UNIQUEIDENTIFIER NOT NULL,
    [ScheduledForUtc] DATETIME2,
    [StartedAtUtc] DATETIME2,
    [EndedAtUtc] DATETIME2,
    [DurationSeconds] INT,
    [CreatedByUserId] UNIQUEIDENTIFIER NOT NULL,
    [CreatedAtUtc] DATETIME2 NOT NULL CONSTRAINT [Meeting_CreatedAtUtc_df] DEFAULT CURRENT_TIMESTAMP,
    [UpdatedAtUtc] DATETIME2 NOT NULL CONSTRAINT [Meeting_UpdatedAtUtc_df] DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT [Meeting_pkey] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [Meeting_TenantId_ShortCode_key] UNIQUE NONCLUSTERED ([TenantId],[ShortCode])
);

-- CreateTable
CREATE TABLE [dbo].[MeetingParticipant] (
    [Id] UNIQUEIDENTIFIER NOT NULL,
    [MeetingId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [DisplayName] NVARCHAR(120) NOT NULL,
    [Role] NVARCHAR(16) NOT NULL,
    [Status] NVARCHAR(16) NOT NULL,
    [JoinOrder] INT NOT NULL,
    [RequestedAtUtc] DATETIME2 NOT NULL CONSTRAINT [MeetingParticipant_RequestedAtUtc_df] DEFAULT CURRENT_TIMESTAMP,
    [AdmittedAtUtc] DATETIME2,
    [JoinedAtUtc] DATETIME2,
    [LeftAtUtc] DATETIME2,
    [AudioEnabled] BIT NOT NULL CONSTRAINT [MeetingParticipant_AudioEnabled_df] DEFAULT 1,
    [VideoEnabled] BIT NOT NULL CONSTRAINT [MeetingParticipant_VideoEnabled_df] DEFAULT 1,
    [ScreenSharing] BIT NOT NULL CONSTRAINT [MeetingParticipant_ScreenSharing_df] DEFAULT 0,
    [HandRaised] BIT NOT NULL CONSTRAINT [MeetingParticipant_HandRaised_df] DEFAULT 0,
    [ConnectionQuality] NVARCHAR(16) NOT NULL CONSTRAINT [MeetingParticipant_ConnectionQuality_df] DEFAULT 'Unknown',
    CONSTRAINT [MeetingParticipant_pkey] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [MeetingParticipant_MeetingId_UserId_key] UNIQUE NONCLUSTERED ([MeetingId],[UserId])
);

-- CreateTable
CREATE TABLE [dbo].[CommunicationAnalyticsSnapshot] (
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [Day] DATE NOT NULL,
    [MessagesSent] INT NOT NULL CONSTRAINT [CommunicationAnalyticsSnapshot_MessagesSent_df] DEFAULT 0,
    [ConversationsStarted] INT NOT NULL CONSTRAINT [CommunicationAnalyticsSnapshot_ConversationsStarted_df] DEFAULT 0,
    [CallsStarted] INT NOT NULL CONSTRAINT [CommunicationAnalyticsSnapshot_CallsStarted_df] DEFAULT 0,
    [CallsEnded] INT NOT NULL CONSTRAINT [CommunicationAnalyticsSnapshot_CallsEnded_df] DEFAULT 0,
    [CallMinutes] INT NOT NULL CONSTRAINT [CommunicationAnalyticsSnapshot_CallMinutes_df] DEFAULT 0,
    [MissedCalls] INT NOT NULL CONSTRAINT [CommunicationAnalyticsSnapshot_MissedCalls_df] DEFAULT 0,
    [MeetingsScheduled] INT NOT NULL CONSTRAINT [CommunicationAnalyticsSnapshot_MeetingsScheduled_df] DEFAULT 0,
    [MeetingsStarted] INT NOT NULL CONSTRAINT [CommunicationAnalyticsSnapshot_MeetingsStarted_df] DEFAULT 0,
    [MeetingsEnded] INT NOT NULL CONSTRAINT [CommunicationAnalyticsSnapshot_MeetingsEnded_df] DEFAULT 0,
    [MeetingMinutes] INT NOT NULL CONSTRAINT [CommunicationAnalyticsSnapshot_MeetingMinutes_df] DEFAULT 0,
    [SupportTicketsOpened] INT NOT NULL CONSTRAINT [CommunicationAnalyticsSnapshot_SupportTicketsOpened_df] DEFAULT 0,
    [SupportTicketsResolved] INT NOT NULL CONSTRAINT [CommunicationAnalyticsSnapshot_SupportTicketsResolved_df] DEFAULT 0,
    [UpdatedAtUtc] DATETIME2 NOT NULL CONSTRAINT [CommunicationAnalyticsSnapshot_UpdatedAtUtc_df] DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT [CommunicationAnalyticsSnapshot_pkey] PRIMARY KEY CLUSTERED ([TenantId],[Day])
);

-- CreateTable
CREATE TABLE [dbo].[SupportTicket] (
    [Id] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [AgentTenantId] UNIQUEIDENTIFIER NOT NULL,
    [OpenedByUserId] UNIQUEIDENTIFIER NOT NULL,
    [AssignedAgentId] UNIQUEIDENTIFIER,
    [ConversationId] UNIQUEIDENTIFIER NOT NULL,
    [Subject] NVARCHAR(200) NOT NULL,
    [Category] NVARCHAR(32) NOT NULL,
    [Priority] NVARCHAR(16) NOT NULL,
    [Status] NVARCHAR(24) NOT NULL,
    [OpenedAtUtc] DATETIME2 NOT NULL CONSTRAINT [SupportTicket_OpenedAtUtc_df] DEFAULT CURRENT_TIMESTAMP,
    [ClaimedAtUtc] DATETIME2,
    [ResolvedAtUtc] DATETIME2,
    [ClosedAtUtc] DATETIME2,
    [UpdatedAtUtc] DATETIME2 NOT NULL CONSTRAINT [SupportTicket_UpdatedAtUtc_df] DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT [SupportTicket_pkey] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [SupportTicket_ConversationId_key] UNIQUE NONCLUSTERED ([ConversationId])
);

-- CreateTable
CREATE TABLE [dbo].[NotificationEntry] (
    [Id] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [Kind] NVARCHAR(64) NOT NULL,
    [Priority] NVARCHAR(16) NOT NULL CONSTRAINT [NotificationEntry_Priority_df] DEFAULT 'Normal',
    [Title] NVARCHAR(200) NOT NULL,
    [Body] NVARCHAR(1000) NOT NULL,
    [MetadataJson] NVARCHAR(max) NOT NULL,
    [SourceEventId] UNIQUEIDENTIFIER NOT NULL,
    [SourceEventType] NVARCHAR(128) NOT NULL,
    [CorrelationId] NVARCHAR(64),
    [ReadAtUtc] DATETIME2,
    [DismissedAtUtc] DATETIME2,
    [CreatedAtUtc] DATETIME2 NOT NULL CONSTRAINT [NotificationEntry_CreatedAtUtc_df] DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT [NotificationEntry_pkey] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [NotificationEntry_TenantId_SourceEventId_UserId_key] UNIQUE NONCLUSTERED ([TenantId],[SourceEventId],[UserId])
);

-- CreateTable
CREATE TABLE [dbo].[MeetingInvitation] (
    [Id] UNIQUEIDENTIFIER NOT NULL,
    [MeetingId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [InviteeEmail] NVARCHAR(320),
    [InviteeUserId] UNIQUEIDENTIFIER,
    [TokenHash] NVARCHAR(128) NOT NULL,
    [ExpiresAtUtc] DATETIME2 NOT NULL,
    [UsedAtUtc] DATETIME2,
    [RevokedAtUtc] DATETIME2,
    [CreatedAtUtc] DATETIME2 NOT NULL CONSTRAINT [MeetingInvitation_CreatedAtUtc_df] DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT [MeetingInvitation_pkey] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [MeetingInvitation_TokenHash_key] UNIQUE NONCLUSTERED ([TokenHash])
);

-- CreateIndex
CREATE NONCLUSTERED INDEX [UserPermissionsProjection_TenantId_IsActive_idx] ON [dbo].[UserPermissionsProjection]([TenantId], [IsActive]);

-- CreateIndex
CREATE NONCLUSTERED INDEX [ProcessedEvent_Source_EventType_idx] ON [dbo].[ProcessedEvent]([Source], [EventType]);

-- CreateIndex
CREATE NONCLUSTERED INDEX [OutboxMessage_PublishedAtUtc_idx] ON [dbo].[OutboxMessage]([PublishedAtUtc]);

-- CreateIndex
CREATE NONCLUSTERED INDEX [OutboxMessage_TenantId_idx] ON [dbo].[OutboxMessage]([TenantId]);

-- CreateIndex
CREATE NONCLUSTERED INDEX [IdempotencyRecord_CreatedAtUtc_idx] ON [dbo].[IdempotencyRecord]([CreatedAtUtc]);

-- CreateIndex
CREATE NONCLUSTERED INDEX [Conversation_TenantId_IsArchived_LastMessageAtUtc_idx] ON [dbo].[Conversation]([TenantId], [IsArchived], [LastMessageAtUtc] DESC);

-- CreateIndex
CREATE NONCLUSTERED INDEX [ConversationParticipant_TenantId_UserId_IsRemoved_idx] ON [dbo].[ConversationParticipant]([TenantId], [UserId], [IsRemoved]);

-- CreateIndex
CREATE NONCLUSTERED INDEX [Message_ConversationId_CreatedAtUtc_idx] ON [dbo].[Message]([ConversationId], [CreatedAtUtc]);

-- CreateIndex
CREATE NONCLUSTERED INDEX [Message_TenantId_SenderId_CreatedAtUtc_idx] ON [dbo].[Message]([TenantId], [SenderId], [CreatedAtUtc]);

-- CreateIndex
CREATE NONCLUSTERED INDEX [MessageReceipt_TenantId_UserId_ReadAtUtc_idx] ON [dbo].[MessageReceipt]([TenantId], [UserId], [ReadAtUtc]);

-- CreateIndex
CREATE NONCLUSTERED INDEX [Call_TenantId_Status_RingingAtUtc_idx] ON [dbo].[Call]([TenantId], [Status], [RingingAtUtc] DESC);

-- CreateIndex
CREATE NONCLUSTERED INDEX [Call_TenantId_CallerUserId_RingingAtUtc_idx] ON [dbo].[Call]([TenantId], [CallerUserId], [RingingAtUtc] DESC);

-- CreateIndex
CREATE NONCLUSTERED INDEX [Call_TenantId_CalleeUserId_RingingAtUtc_idx] ON [dbo].[Call]([TenantId], [CalleeUserId], [RingingAtUtc] DESC);

-- CreateIndex
CREATE NONCLUSTERED INDEX [CallParticipant_TenantId_UserId_LeftAtUtc_idx] ON [dbo].[CallParticipant]([TenantId], [UserId], [LeftAtUtc]);

-- CreateIndex
CREATE NONCLUSTERED INDEX [Meeting_TenantId_Status_ScheduledForUtc_idx] ON [dbo].[Meeting]([TenantId], [Status], [ScheduledForUtc]);

-- CreateIndex
CREATE NONCLUSTERED INDEX [Meeting_TenantId_HostUserId_idx] ON [dbo].[Meeting]([TenantId], [HostUserId]);

-- CreateIndex
CREATE NONCLUSTERED INDEX [MeetingParticipant_TenantId_UserId_Status_idx] ON [dbo].[MeetingParticipant]([TenantId], [UserId], [Status]);

-- CreateIndex
CREATE NONCLUSTERED INDEX [MeetingParticipant_MeetingId_Status_idx] ON [dbo].[MeetingParticipant]([MeetingId], [Status]);

-- CreateIndex
CREATE NONCLUSTERED INDEX [SupportTicket_TenantId_Status_OpenedAtUtc_idx] ON [dbo].[SupportTicket]([TenantId], [Status], [OpenedAtUtc] DESC);

-- CreateIndex
CREATE NONCLUSTERED INDEX [SupportTicket_AgentTenantId_Status_OpenedAtUtc_idx] ON [dbo].[SupportTicket]([AgentTenantId], [Status], [OpenedAtUtc] DESC);

-- CreateIndex
CREATE NONCLUSTERED INDEX [SupportTicket_AgentTenantId_AssignedAgentId_Status_idx] ON [dbo].[SupportTicket]([AgentTenantId], [AssignedAgentId], [Status]);

-- CreateIndex
CREATE NONCLUSTERED INDEX [NotificationEntry_TenantId_UserId_ReadAtUtc_CreatedAtUtc_idx] ON [dbo].[NotificationEntry]([TenantId], [UserId], [ReadAtUtc], [CreatedAtUtc] DESC);

-- CreateIndex
CREATE NONCLUSTERED INDEX [MeetingInvitation_MeetingId_ExpiresAtUtc_idx] ON [dbo].[MeetingInvitation]([MeetingId], [ExpiresAtUtc]);

-- AddForeignKey
ALTER TABLE [dbo].[ConversationParticipant] ADD CONSTRAINT [ConversationParticipant_ConversationId_fkey] FOREIGN KEY ([ConversationId]) REFERENCES [dbo].[Conversation]([Id]) ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE [dbo].[Message] ADD CONSTRAINT [Message_ConversationId_fkey] FOREIGN KEY ([ConversationId]) REFERENCES [dbo].[Conversation]([Id]) ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE [dbo].[MessageReceipt] ADD CONSTRAINT [MessageReceipt_MessageId_fkey] FOREIGN KEY ([MessageId]) REFERENCES [dbo].[Message]([Id]) ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE [dbo].[CallParticipant] ADD CONSTRAINT [CallParticipant_CallId_fkey] FOREIGN KEY ([CallId]) REFERENCES [dbo].[Call]([Id]) ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE [dbo].[MeetingParticipant] ADD CONSTRAINT [MeetingParticipant_MeetingId_fkey] FOREIGN KEY ([MeetingId]) REFERENCES [dbo].[Meeting]([Id]) ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE [dbo].[MeetingInvitation] ADD CONSTRAINT [MeetingInvitation_MeetingId_fkey] FOREIGN KEY ([MeetingId]) REFERENCES [dbo].[Meeting]([Id]) ON DELETE CASCADE ON UPDATE CASCADE;

COMMIT TRAN;

END TRY
BEGIN CATCH

IF @@TRANCOUNT > 0
BEGIN
    ROLLBACK TRAN;
END;
THROW

END CATCH
