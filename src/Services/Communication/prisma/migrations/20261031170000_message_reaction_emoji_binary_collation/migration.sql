-- Hallazgo del E2E de F8: con la intercalación por defecto (SQL_Latin1_General_CP1_CI_AS) los emojis fuera
-- del plano básico (😂, 😮, 🎉…) comparan iguales: una persona solo podía poner uno por mensaje y quitar uno
-- borraba los otros. Intercalación binaria en `Emoji` (la columna solo guarda emojis, nunca texto a ordenar).
BEGIN TRY

BEGIN TRAN;

ALTER TABLE [dbo].[MessageReaction] DROP CONSTRAINT [MessageReaction_MessageId_UserId_Emoji_key];

ALTER TABLE [dbo].[MessageReaction] ALTER COLUMN [Emoji] NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL;

ALTER TABLE [dbo].[MessageReaction] ADD CONSTRAINT [MessageReaction_MessageId_UserId_Emoji_key]
  UNIQUE NONCLUSTERED ([MessageId], [UserId], [Emoji]);

COMMIT TRAN;

END TRY
BEGIN CATCH

IF @@TRANCOUNT > 0
BEGIN
    ROLLBACK TRAN;
END;
THROW

END CATCH
