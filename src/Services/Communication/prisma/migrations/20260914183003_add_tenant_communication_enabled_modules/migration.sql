BEGIN TRY

BEGIN TRAN;

-- AlterTable
ALTER TABLE [dbo].[TenantCommunicationLimits] ADD [EnabledModulesJson] NVARCHAR(max) NOT NULL CONSTRAINT [TenantCommunicationLimits_EnabledModulesJson_df] DEFAULT '[]';

COMMIT TRAN;

END TRY
BEGIN CATCH

IF @@TRANCOUNT > 0
BEGIN
    ROLLBACK TRAN;
END;
THROW

END CATCH
