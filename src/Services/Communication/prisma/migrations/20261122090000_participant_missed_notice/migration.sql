BEGIN TRY

BEGIN TRAN;

-- D-N4 (mensajes perdidos): hasta cuándo ya se avisó al participante de mensajes sin leer. Sin relleno.
ALTER TABLE [dbo].[ConversationParticipant] ADD [MissedNoticeAtUtc] DATETIME2 NULL;

COMMIT TRAN;

END TRY
BEGIN CATCH

IF @@TRANCOUNT > 0
BEGIN
    ROLLBACK TRAN;
END;
THROW

END CATCH
