BEGIN TRY

BEGIN TRAN;

-- AlterTable: F8a dueño derivado de la clave (meeting / ticket).
ALTER TABLE [dbo].[Conversation] ADD
    [MeetingId] UNIQUEIDENTIFIER NULL,
    [SupportTicketId] UNIQUEIDENTIFIER NULL;

-- CreateIndex
CREATE NONCLUSTERED INDEX [Conversation_TenantId_Kind_IsArchived_LastMessageAtUtc_idx] ON [dbo].[Conversation]([TenantId], [Kind], [IsArchived], [LastMessageAtUtc] DESC);
CREATE NONCLUSTERED INDEX [Conversation_TenantId_MeetingId_idx] ON [dbo].[Conversation]([TenantId], [MeetingId]);
CREATE NONCLUSTERED INDEX [Conversation_TenantId_SupportTicketId_idx] ON [dbo].[Conversation]([TenantId], [SupportTicketId]);

COMMIT TRAN;

END TRY
BEGIN CATCH

IF @@TRANCOUNT > 0
BEGIN
    ROLLBACK TRAN;
END;
THROW

END CATCH

-- Backfill: las columnas nuevas se referencian por sp_executesql (no compilan en el mismo lote del ALTER).
EXEC sp_executesql N'
UPDATE [dbo].[Conversation]
SET [MeetingId] = TRY_CONVERT(UNIQUEIDENTIFIER, SUBSTRING([UniquenessKey], 9, 64))
WHERE [Kind] = N''Meeting'' AND [UniquenessKey] LIKE N''meeting:%'' AND [MeetingId] IS NULL;

UPDATE [dbo].[Conversation]
SET [SupportTicketId] = TRY_CONVERT(UNIQUEIDENTIFIER, SUBSTRING([UniquenessKey], 9, 64))
WHERE [Kind] = N''Support'' AND [UniquenessKey] LIKE N''support:%'' AND [SupportTicketId] IS NULL;

UPDATE c SET c.[IsArchived] = 1, c.[UpdatedAtUtc] = SYSUTCDATETIME()
FROM [dbo].[Conversation] c
JOIN [dbo].[Meeting] m ON m.[Id] = c.[MeetingId] AND m.[TenantId] = c.[TenantId]
WHERE c.[Kind] = N''Meeting'' AND c.[IsArchived] = 0 AND m.[Status] IN (N''Ended'', N''Cancelled'', N''Expired'');
';
