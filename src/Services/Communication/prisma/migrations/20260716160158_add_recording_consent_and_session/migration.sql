BEGIN TRY

BEGIN TRAN;

-- AlterTable
ALTER TABLE [dbo].[TenantCommunicationSettings] ADD [RecordingConsentPolicy] NVARCHAR(32) NOT NULL CONSTRAINT [TenantCommunicationSettings_RecordingConsentPolicy_df] DEFAULT 'NoRejections';

-- CreateTable
CREATE TABLE [dbo].[RecordingConsentEvent] (
    [Id] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [Scope] NVARCHAR(16) NOT NULL,
    [ScopeId] UNIQUEIDENTIFIER NOT NULL,
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [Response] NVARCHAR(16) NOT NULL,
    [RespondedAtUtc] DATETIME2 NOT NULL,
    [RecordedAtUtc] DATETIME2 NOT NULL CONSTRAINT [RecordingConsentEvent_RecordedAtUtc_df] DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT [RecordingConsentEvent_pkey] PRIMARY KEY CLUSTERED ([Id])
);

-- CreateTable
CREATE TABLE [dbo].[RecordingSession] (
    [Id] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [Scope] NVARCHAR(16) NOT NULL,
    [ScopeId] UNIQUEIDENTIFIER NOT NULL,
    [State] NVARCHAR(16) NOT NULL,
    [RequestedByUserId] UNIQUEIDENTIFIER NOT NULL,
    [RequestedAtUtc] DATETIME2 NOT NULL,
    [StartedAtUtc] DATETIME2,
    [StoppedAtUtc] DATETIME2,
    [RecordingFileId] UNIQUEIDENTIFIER,
    [DurationSeconds] INT,
    [FailureReason] NVARCHAR(500),
    CONSTRAINT [RecordingSession_pkey] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [RecordingSession_Scope_ScopeId_key] UNIQUE NONCLUSTERED ([Scope],[ScopeId])
);

-- CreateIndex
CREATE NONCLUSTERED INDEX [RecordingConsentEvent_TenantId_Scope_ScopeId_RespondedAtUtc_idx] ON [dbo].[RecordingConsentEvent]([TenantId], [Scope], [ScopeId], [RespondedAtUtc]);

-- CreateIndex
CREATE NONCLUSTERED INDEX [RecordingSession_TenantId_Scope_ScopeId_idx] ON [dbo].[RecordingSession]([TenantId], [Scope], [ScopeId]);

COMMIT TRAN;

END TRY
BEGIN CATCH

IF @@TRANCOUNT > 0
BEGIN
    ROLLBACK TRAN;
END;
THROW

END CATCH
