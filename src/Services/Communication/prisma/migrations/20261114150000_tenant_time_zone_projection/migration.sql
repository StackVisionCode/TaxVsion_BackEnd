BEGIN TRY

BEGIN TRAN;

-- Zona IANA de cada oficina proyectada desde Tenant (TenantCreated/TenantUpdated + reconciliación).
CREATE TABLE [dbo].[TenantTimeZoneProjection] (
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [TimeZoneId] NVARCHAR(64) NOT NULL,
    [SourceVersion] BIGINT,
    [SourceUpdatedAtUtc] DATETIME2 NOT NULL,
    CONSTRAINT [TenantTimeZoneProjection_pkey] PRIMARY KEY CLUSTERED ([TenantId])
);

COMMIT TRAN;

END TRY
BEGIN CATCH

IF @@TRANCOUNT > 0
BEGIN
    ROLLBACK TRAN;
END;
THROW

END CATCH
