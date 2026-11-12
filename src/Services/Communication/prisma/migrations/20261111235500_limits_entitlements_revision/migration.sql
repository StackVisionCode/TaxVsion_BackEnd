BEGIN TRY

BEGIN TRAN;

-- Revisión de entitlements aplicada a los límites: un evento viejo que llega tarde ya no los pisa.
ALTER TABLE [dbo].[TenantCommunicationLimits] ADD [LastEntitlementsRevision] BIGINT NULL;

COMMIT TRAN;

END TRY
BEGIN CATCH

IF @@TRANCOUNT > 0
BEGIN
    ROLLBACK TRAN;
END;
THROW

END CATCH
