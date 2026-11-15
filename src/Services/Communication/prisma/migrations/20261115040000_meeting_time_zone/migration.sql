BEGIN TRY

BEGIN TRAN;

-- Zona de la cita para los correos de su sala (TZ03-H7); null = la de la oficina.
ALTER TABLE [dbo].[Meeting] ADD [TimeZoneId] NVARCHAR(64) NULL;

COMMIT TRAN;

END TRY
BEGIN CATCH

IF @@TRANCOUNT > 0
BEGIN
    ROLLBACK TRAN;
END;
THROW

END CATCH
