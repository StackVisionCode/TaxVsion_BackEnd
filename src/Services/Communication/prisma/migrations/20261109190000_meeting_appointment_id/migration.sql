BEGIN TRY

BEGIN TRAN;

-- Sala de una cita de Calendar: con esto el aviso original y la reparacion encuentran la misma sala.
ALTER TABLE [dbo].[Meeting] ADD [AppointmentId] UNIQUEIDENTIFIER NULL;

CREATE NONCLUSTERED INDEX [Meeting_TenantId_AppointmentId_idx] ON [dbo].[Meeting]([TenantId], [AppointmentId]);

COMMIT TRAN;

END TRY
BEGIN CATCH

IF @@TRANCOUNT > 0
BEGIN
    ROLLBACK TRAN;
END;
THROW

END CATCH
