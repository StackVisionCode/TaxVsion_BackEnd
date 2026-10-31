BEGIN TRY

BEGIN TRAN;

-- AlterTable: D-58 en calls (F4), misma regla que MeetingParticipant.CustomerId.
ALTER TABLE [dbo].[CallParticipant] ADD [CustomerId] UNIQUEIDENTIFIER;

-- CreateIndex
CREATE NONCLUSTERED INDEX [CallParticipant_TenantId_CustomerId_idx] ON [dbo].[CallParticipant]([TenantId], [CustomerId]);

COMMIT TRAN;

END TRY
BEGIN CATCH

IF @@TRANCOUNT > 0
BEGIN
    ROLLBACK TRAN;
END;
THROW

END CATCH
