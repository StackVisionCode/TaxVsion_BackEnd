BEGIN TRY

BEGIN TRAN;

-- Plan a la medida: qué claves de la oficina son una excepción y no el valor de su plan. Solo columna nueva.
ALTER TABLE [dbo].[TenantCommunicationLimits] ADD [OverriddenKeysJson] NVARCHAR(max) NOT NULL CONSTRAINT [TenantCommunicationLimits_OverriddenKeysJson_df] DEFAULT '[]';

COMMIT TRAN;

END TRY
BEGIN CATCH

IF @@TRANCOUNT > 0
BEGIN
    ROLLBACK TRAN;
END;
THROW

END CATCH
