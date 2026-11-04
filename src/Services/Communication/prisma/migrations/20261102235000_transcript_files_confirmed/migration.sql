BEGIN TRY

BEGIN TRAN;

-- MNT-01 (E-292): cuando CloudStorage confirma el .txt del transcript. Sin confirmar, el barrido
-- verifica que exista (los rechazos de SaveFileRequested no avisaban y quedaban punteros rotos).
ALTER TABLE [dbo].[TranscriptArtifact] ADD [FilesConfirmedAtUtc] DATETIME2 NULL;

CREATE NONCLUSTERED INDEX [TranscriptArtifact_State_FilesConfirmedAtUtc_idx] ON [dbo].[TranscriptArtifact]([State], [FilesConfirmedAtUtc]);

COMMIT TRAN;

END TRY
BEGIN CATCH

IF @@TRANCOUNT > 0
BEGIN
    ROLLBACK TRAN;
END;
THROW

END CATCH
