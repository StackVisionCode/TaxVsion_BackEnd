BEGIN TRY

BEGIN TRAN;

-- Purga de retención de la bandeja: leídas viejas y no leídas viejas. Solo índice, sin datos.
CREATE NONCLUSTERED INDEX [NotificationEntry_ReadAtUtc_CreatedAtUtc_idx] ON [dbo].[NotificationEntry]([ReadAtUtc], [CreatedAtUtc]);

COMMIT TRAN;

END TRY
BEGIN CATCH

IF @@TRANCOUNT > 0
BEGIN
    ROLLBACK TRAN;
END;
THROW

END CATCH
