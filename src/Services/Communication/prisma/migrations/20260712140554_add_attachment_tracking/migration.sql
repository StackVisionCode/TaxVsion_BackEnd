BEGIN TRY

BEGIN TRAN;

-- CreateTable
CREATE TABLE [dbo].[AttachmentTracking] (
    [FileId] UNIQUEIDENTIFIER NOT NULL,
    [MessageId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [Status] NVARCHAR(16) NOT NULL CONSTRAINT [AttachmentTracking_Status_df] DEFAULT 'Pending',
    [CreatedAtUtc] DATETIME2 NOT NULL CONSTRAINT [AttachmentTracking_CreatedAtUtc_df] DEFAULT CURRENT_TIMESTAMP,
    [UpdatedAtUtc] DATETIME2 NOT NULL CONSTRAINT [AttachmentTracking_UpdatedAtUtc_df] DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT [AttachmentTracking_pkey] PRIMARY KEY CLUSTERED ([FileId])
);

-- CreateIndex
CREATE NONCLUSTERED INDEX [AttachmentTracking_MessageId_idx] ON [dbo].[AttachmentTracking]([MessageId]);

-- CreateIndex
CREATE NONCLUSTERED INDEX [AttachmentTracking_TenantId_Status_idx] ON [dbo].[AttachmentTracking]([TenantId], [Status]);

COMMIT TRAN;

END TRY
BEGIN CATCH

IF @@TRANCOUNT > 0
BEGIN
    ROLLBACK TRAN;
END;
THROW

END CATCH
