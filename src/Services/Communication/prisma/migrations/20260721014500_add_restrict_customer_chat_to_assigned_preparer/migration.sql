BEGIN TRY

BEGIN TRAN;

-- AlterTable
ALTER TABLE [dbo].[TenantCommunicationSettings] ADD [RestrictCustomerChatToAssignedPreparer] BIT NOT NULL CONSTRAINT [TenantCommunicationSettings_RestrictCustomerChatToAssignedPreparer_df] DEFAULT 0;

COMMIT TRAN;

END TRY
BEGIN CATCH

IF @@TRANCOUNT > 0
BEGIN
    ROLLBACK TRAN;
END;
THROW

END CATCH
