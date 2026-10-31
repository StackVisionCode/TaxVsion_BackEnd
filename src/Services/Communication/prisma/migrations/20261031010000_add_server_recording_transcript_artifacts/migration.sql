BEGIN TRY

BEGIN TRAN;

-- AlterTable: F7 grabación server-side, límites, composición, retención y notas de voz (plataforma).
ALTER TABLE [dbo].[PlatformCommunicationSettings] ADD
    [ServerSideRecordingEnabled] BIT NOT NULL CONSTRAINT [PlatformCommunicationSettings_ServerSideRecordingEnabled_df] DEFAULT 1,
    [RecordingLimitWarningMinutes] INT NOT NULL CONSTRAINT [PlatformCommunicationSettings_RecordingLimitWarningMinutes_df] DEFAULT 5,
    [RecordingLimitToleranceSeconds] INT NOT NULL CONSTRAINT [PlatformCommunicationSettings_RecordingLimitToleranceSeconds_df] DEFAULT 120,
    [RecordingCompositionWidth] INT NOT NULL CONSTRAINT [PlatformCommunicationSettings_RecordingCompositionWidth_df] DEFAULT 1280,
    [RecordingCompositionHeight] INT NOT NULL CONSTRAINT [PlatformCommunicationSettings_RecordingCompositionHeight_df] DEFAULT 720,
    [RecordingCompositionFps] INT NOT NULL CONSTRAINT [PlatformCommunicationSettings_RecordingCompositionFps_df] DEFAULT 25,
    [RecordingCompositionVideoKbps] INT NOT NULL CONSTRAINT [PlatformCommunicationSettings_RecordingCompositionVideoKbps_df] DEFAULT 1500,
    [RecordingCompositionAudioKbps] INT NOT NULL CONSTRAINT [PlatformCommunicationSettings_RecordingCompositionAudioKbps_df] DEFAULT 128,
    [RecordingCompositionFormat] NVARCHAR(8) NOT NULL CONSTRAINT [PlatformCommunicationSettings_RecordingCompositionFormat_df] DEFAULT N'mp4',
    [RecordingCaptureSpatialLayer] INT NOT NULL CONSTRAINT [PlatformCommunicationSettings_RecordingCaptureSpatialLayer_df] DEFAULT 2,
    [RecordingTracksRetentionDays] INT NOT NULL CONSTRAINT [PlatformCommunicationSettings_RecordingTracksRetentionDays_df] DEFAULT 7,
    [VoiceNoteTranscriptEnabled] BIT NOT NULL CONSTRAINT [PlatformCommunicationSettings_VoiceNoteTranscriptEnabled_df] DEFAULT 1,
    [VoiceNoteTranscriptMaxSeconds] INT NOT NULL CONSTRAINT [PlatformCommunicationSettings_VoiceNoteTranscriptMaxSeconds_df] DEFAULT 600,
    [TranscriptWorkerTimeoutFactorPct] INT NOT NULL CONSTRAINT [PlatformCommunicationSettings_TranscriptWorkerTimeoutFactorPct_df] DEFAULT 150,
    [RecordingAvailabilityTimeoutMinutes] INT NOT NULL CONSTRAINT [PlatformCommunicationSettings_RecordingAvailabilityTimeoutMinutes_df] DEFAULT 60;

-- AlterTable: F7 overrides de oficina (NULL = hereda la plataforma).
ALTER TABLE [dbo].[TenantCommunicationSettings] ADD
    [TranscriptPrompt] NVARCHAR(500) NULL,
    [VoiceNoteTranscriptEnabled] BIT NULL;

-- AlterTable: F7 captura server-side, límite, compuesto y retención por sesión de grabación.
ALTER TABLE [dbo].[RecordingSession] ADD
    [CaptureMode] NVARCHAR(16) NOT NULL CONSTRAINT [RecordingSession_CaptureMode_df] DEFAULT N'Client',
    [MaxDurationSeconds] INT NULL,
    [LimitWarnedAtUtc] DATETIME2 NULL,
    [StopReason] NVARCHAR(32) NULL,
    [ComposedFileId] UNIQUEIDENTIFIER NULL,
    [ComposedSizeBytes] BIGINT NULL,
    [ComposedFormat] NVARCHAR(8) NULL,
    [TracksManifestJson] NVARCHAR(MAX) NULL,
    [ComposedAtUtc] DATETIME2 NULL,
    [ReadyAtUtc] DATETIME2 NULL,
    [TracksPurgedAtUtc] DATETIME2 NULL,
    [PurgedAtUtc] DATETIME2 NULL;

-- CreateIndex: el scheduler de grabación barre por estado y modo; los eventos de CloudStorage buscan por compuesto.
CREATE NONCLUSTERED INDEX [RecordingSession_State_CaptureMode_idx] ON [dbo].[RecordingSession]([State], [CaptureMode]);
CREATE NONCLUSTERED INDEX [RecordingSession_ComposedFileId_idx] ON [dbo].[RecordingSession]([ComposedFileId]);

-- CreateTable: F7 (T-34) transcript con estado propio, separado de la grabación.
CREATE TABLE [dbo].[TranscriptArtifact] (
    [Id] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [Scope] NVARCHAR(16) NOT NULL,
    [ScopeId] UNIQUEIDENTIFIER NOT NULL,
    [RecordingSessionId] UNIQUEIDENTIFIER,
    [State] NVARCHAR(16) NOT NULL,
    [TxtFileId] UNIQUEIDENTIFIER,
    [JsonFileId] UNIQUEIDENTIFIER,
    [Language] NVARCHAR(16),
    [Model] NVARCHAR(64),
    [Vad] BIT NOT NULL CONSTRAINT [TranscriptArtifact_Vad_df] DEFAULT 0,
    [DurationSeconds] INT,
    [WordCount] INT,
    [SegmentCount] INT,
    [SpeakerCount] INT,
    [RealtimeFactor] FLOAT(53),
    [FailureReason] NVARCHAR(64),
    [CreatedAtUtc] DATETIME2 NOT NULL CONSTRAINT [TranscriptArtifact_CreatedAtUtc_df] DEFAULT CURRENT_TIMESTAMP,
    [CompletedAtUtc] DATETIME2,
    CONSTRAINT [TranscriptArtifact_pkey] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [TranscriptArtifact_Scope_ScopeId_key] UNIQUE NONCLUSTERED ([Scope],[ScopeId])
);
CREATE NONCLUSTERED INDEX [TranscriptArtifact_TenantId_Scope_ScopeId_idx] ON [dbo].[TranscriptArtifact]([TenantId], [Scope], [ScopeId]);

-- AlterTable: F7 (D-27) transcript en línea de la nota de voz.
ALTER TABLE [dbo].[Message] ADD
    [TranscriptText] NVARCHAR(4000) NULL,
    [TranscriptState] NVARCHAR(16) NULL,
    [TranscriptLanguage] NVARCHAR(16) NULL;

-- La fila global sube de version para que las caches por oficina recalculen.
UPDATE [dbo].[PlatformCommunicationSettings] SET [Version] = [Version] + 1 WHERE [Id] = N'global';

COMMIT TRAN;

END TRY
BEGIN CATCH

IF @@TRANCOUNT > 0
BEGIN
    ROLLBACK TRAN;
END;
THROW

END CATCH
