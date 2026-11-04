BEGIN TRY

BEGIN TRAN;

-- D-72: PlatformAdmin puede dejar que una oficina gestione su propia retención (default: no).
ALTER TABLE [dbo].[PlatformCommunicationSettings] ADD [RetentionManagedByTenant] BIT NOT NULL CONSTRAINT [PlatformCommunicationSettings_RetentionManagedByTenant_df] DEFAULT 0;

-- Excepción por oficina (NULL = la global).
ALTER TABLE [dbo].[TenantCommunicationConstraints] ADD [RetentionManagedByTenant] BIT NULL;

-- Valores de la oficina (NULL = no fijado); solo rigen con la capacidad encendida.
ALTER TABLE [dbo].[TenantCommunicationSettings] ADD
    [PurgeEnabled] BIT NULL,
    [MessageRetentionDays] INT NULL,
    [RecordingRetentionDays] INT NULL;

-- "Keep the chat after the meeting" nunca se leyó: el chat de la reunión sigue la retención de mensajes.
ALTER TABLE [dbo].[TenantCommunicationSettings] DROP CONSTRAINT [TenantCommunicationSettings_PersistChatOnEnd_df];
ALTER TABLE [dbo].[TenantCommunicationSettings] DROP COLUMN [PersistChatOnEnd];

COMMIT TRAN;

END TRY
BEGIN CATCH

IF @@TRANCOUNT > 0
BEGIN
    ROLLBACK TRAN;
END;
THROW

END CATCH
