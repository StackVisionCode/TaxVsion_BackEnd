BEGIN TRY

BEGIN TRAN;

-- AlterTable
ALTER TABLE [dbo].[Message] ADD [ForwardedFromMessageId] UNIQUEIDENTIFIER,
[IsPinned] BIT NOT NULL CONSTRAINT [Message_IsPinned_df] DEFAULT 0,
[PinnedAtUtc] DATETIME2,
[PinnedByUserId] UNIQUEIDENTIFIER;

-- CreateTable
CREATE TABLE [dbo].[MessageReaction] (
    [Id] UNIQUEIDENTIFIER NOT NULL,
    [MessageId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [Emoji] NVARCHAR(16) NOT NULL,
    [CreatedAtUtc] DATETIME2 NOT NULL CONSTRAINT [MessageReaction_CreatedAtUtc_df] DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT [MessageReaction_pkey] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [MessageReaction_MessageId_UserId_Emoji_key] UNIQUE NONCLUSTERED ([MessageId],[UserId],[Emoji])
);

-- CreateIndex
CREATE NONCLUSTERED INDEX [MessageReaction_TenantId_MessageId_idx] ON [dbo].[MessageReaction]([TenantId], [MessageId]);

-- CreateIndex
CREATE NONCLUSTERED INDEX [Message_ConversationId_PinnedAtUtc_idx] ON [dbo].[Message]([ConversationId], [PinnedAtUtc] DESC);

-- AddForeignKey
ALTER TABLE [dbo].[MessageReaction] ADD CONSTRAINT [MessageReaction_MessageId_fkey] FOREIGN KEY ([MessageId]) REFERENCES [dbo].[Message]([Id]) ON DELETE CASCADE ON UPDATE CASCADE;

COMMIT TRAN;

END TRY
BEGIN CATCH

IF @@TRANCOUNT > 0
BEGIN
    ROLLBACK TRAN;
END;
THROW

END CATCH
