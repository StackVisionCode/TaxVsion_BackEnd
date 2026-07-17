BEGIN TRY

BEGIN TRAN;

-- AlterTable
ALTER TABLE [dbo].[UserDirectoryEntry] ADD [ActorType] NVARCHAR(32) NOT NULL CONSTRAINT [UserDirectoryEntry_ActorType_df] DEFAULT 'TenantEmployee';

-- CreateTable
CREATE TABLE [dbo].[CustomerDirectoryEntry] (
    [CustomerId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [DisplayName] NVARCHAR(200) NOT NULL,
    [Email] NVARCHAR(320) NOT NULL,
    [IsActive] BIT NOT NULL CONSTRAINT [CustomerDirectoryEntry_IsActive_df] DEFAULT 1,
    [UpdatedAtUtc] DATETIME2 NOT NULL CONSTRAINT [CustomerDirectoryEntry_UpdatedAtUtc_df] DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT [CustomerDirectoryEntry_pkey] PRIMARY KEY CLUSTERED ([CustomerId])
);

-- CreateIndex
CREATE NONCLUSTERED INDEX [CustomerDirectoryEntry_TenantId_IsActive_idx] ON [dbo].[CustomerDirectoryEntry]([TenantId], [IsActive]);

-- CreateIndex
CREATE NONCLUSTERED INDEX [CustomerDirectoryEntry_TenantId_DisplayName_idx] ON [dbo].[CustomerDirectoryEntry]([TenantId], [DisplayName]);

COMMIT TRAN;

END TRY
BEGIN CATCH

IF @@TRANCOUNT > 0
BEGIN
    ROLLBACK TRAN;
END;
THROW

END CATCH
