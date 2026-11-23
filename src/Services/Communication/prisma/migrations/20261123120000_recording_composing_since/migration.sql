BEGIN TRY

BEGIN TRAN;

-- Inicio de la composición vigente: el plazo deja de medirse desde que se detuvo la grabación. Solo columna nueva.
ALTER TABLE [dbo].[RecordingSession] ADD [ComposingSinceUtc] DATETIME2;

COMMIT TRAN;

END TRY
BEGIN CATCH

IF @@TRANCOUNT > 0
BEGIN
    ROLLBACK TRAN;
END;
THROW

END CATCH
