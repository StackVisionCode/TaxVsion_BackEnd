BEGIN TRY

BEGIN TRAN;

-- Fecha (occurredOn) del ultimo aviso de estado aplicado en cada proyeccion: con prefetch y reintentos un
-- desactivar viejo podia ganarle a un reactivar nuevo. Null = sin aviso aplicado (el proximo gana).
ALTER TABLE [dbo].[UserPermissionsProjection] ADD [StatusChangedAtUtc] DATETIME2 NULL;
ALTER TABLE [dbo].[UserDirectoryEntry] ADD [StatusChangedAtUtc] DATETIME2 NULL;
ALTER TABLE [dbo].[CustomerDirectoryEntry] ADD [StatusChangedAtUtc] DATETIME2 NULL;
ALTER TABLE [dbo].[CustomerPortalAccount] ADD [StatusChangedAtUtc] DATETIME2 NULL;

COMMIT TRAN;

END TRY
BEGIN CATCH

IF @@TRANCOUNT > 0
BEGIN
    ROLLBACK TRAN;
END;
THROW

END CATCH
