BEGIN TRY

BEGIN TRAN;

-- D-63 (2026-10-30): el mesh solo para dos participantes; 3 o mas van siempre por el SFU.
-- Nuevo default de la columna y actualizacion de la fila global existente (sube Version para invalidar caches).
ALTER TABLE [dbo].[PlatformCommunicationSettings] DROP CONSTRAINT [PlatformCommunicationSettings_MeshMaxParticipants_df];
ALTER TABLE [dbo].[PlatformCommunicationSettings] ADD CONSTRAINT [PlatformCommunicationSettings_MeshMaxParticipants_df] DEFAULT 2 FOR [MeshMaxParticipants];
UPDATE [dbo].[PlatformCommunicationSettings] SET [MeshMaxParticipants] = 2, [Version] = [Version] + 1, [UpdatedAtUtc] = SYSUTCDATETIME() WHERE [Id] = 'global' AND [MeshMaxParticipants] > 2;

COMMIT TRAN;

END TRY
BEGIN CATCH

IF @@TRANCOUNT > 0
BEGIN
    ROLLBACK TRAN;
END;
THROW

END CATCH
