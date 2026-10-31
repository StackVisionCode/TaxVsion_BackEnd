BEGIN TRY

BEGIN TRAN;

-- AlterTable: F5 (D-50) politica de reacciones efimeras del meeting (plataforma).
ALTER TABLE [dbo].[PlatformCommunicationSettings] ADD
    [ReactionsEnabled] BIT NOT NULL CONSTRAINT [PlatformCommunicationSettings_ReactionsEnabled_df] DEFAULT 1,
    [ReactionDurationSeconds] INT NOT NULL CONSTRAINT [PlatformCommunicationSettings_ReactionDurationSeconds_df] DEFAULT 4,
    [ReactionsPerUserPer10s] INT NOT NULL CONSTRAINT [PlatformCommunicationSettings_ReactionsPerUserPer10s_df] DEFAULT 5,
    [ReactionsPerMeetingPerMinute] INT NOT NULL CONSTRAINT [PlatformCommunicationSettings_ReactionsPerMeetingPerMinute_df] DEFAULT 300,
    [MeetingReactionCatalog] NVARCHAR(1024) NOT NULL CONSTRAINT [PlatformCommunicationSettings_MeetingReactionCatalog_df] DEFAULT N'["👍","👏","❤️","😂","😮","🎉","🙏","🤔","✅","🔥"]';

-- AlterTable: F5 (D-50) overrides de oficina (NULL = hereda la plataforma).
ALTER TABLE [dbo].[TenantCommunicationSettings] ADD
    [ReactionsEnabled] BIT NULL,
    [MeetingReactionCatalog] NVARCHAR(1024) NULL;

-- AlterTable: F5 (D-49) instante en que el participante levanto la mano (cola del host).
ALTER TABLE [dbo].[MeetingParticipant] ADD
    [HandRaisedAtUtc] DATETIME2 NULL;

-- La fila global sube de version para que las caches por oficina recalculen la politica efectiva.
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
