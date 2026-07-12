/*
  Warnings:

  - Added the required column `ConversationId` to the `AttachmentTracking` table without a default value. This is not possible if the table is not empty.

*/
BEGIN TRY

BEGIN TRAN;

-- AlterTable
ALTER TABLE [dbo].[AttachmentTracking] ADD [ConversationId] UNIQUEIDENTIFIER NOT NULL;

COMMIT TRAN;

END TRY
BEGIN CATCH

IF @@TRANCOUNT > 0
BEGIN
    ROLLBACK TRAN;
END;
THROW

END CATCH
