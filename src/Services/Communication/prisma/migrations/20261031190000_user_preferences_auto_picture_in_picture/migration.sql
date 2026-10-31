-- F9: la preferencia "ventana flotante al ocultar la pestaña" pasa del localStorage a la cuenta del usuario.
BEGIN TRY

BEGIN TRAN;

ALTER TABLE [dbo].[UserCommunicationPreferences] ADD [AutoPictureInPicture] BIT NOT NULL CONSTRAINT [UserCommunicationPreferences_AutoPictureInPicture_df] DEFAULT 0;

COMMIT TRAN;

END TRY
BEGIN CATCH

IF @@TRANCOUNT > 0
BEGIN
    ROLLBACK TRAN;
END;
THROW

END CATCH
