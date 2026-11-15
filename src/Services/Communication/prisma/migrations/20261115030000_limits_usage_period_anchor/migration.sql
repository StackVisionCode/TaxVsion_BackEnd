BEGIN TRY

BEGIN TRAN;

-- Ancla del cupo mensual de minutos (inicio del período de la suscripción); null = mes UTC.
ALTER TABLE [dbo].[TenantCommunicationLimits] ADD [UsagePeriodAnchorUtc] DATETIME2 NULL;

COMMIT TRAN;

END TRY
BEGIN CATCH

IF @@TRANCOUNT > 0
BEGIN
    ROLLBACK TRAN;
END;
THROW

END CATCH
