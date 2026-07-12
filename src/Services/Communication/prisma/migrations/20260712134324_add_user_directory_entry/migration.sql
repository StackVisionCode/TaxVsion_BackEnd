BEGIN TRY

BEGIN TRAN;

-- CreateTable
CREATE TABLE [dbo].[UserDirectoryEntry] (
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [DisplayName] NVARCHAR(200) NOT NULL,
    [Email] NVARCHAR(320) NOT NULL,
    [IsActive] BIT NOT NULL CONSTRAINT [UserDirectoryEntry_IsActive_df] DEFAULT 1,
    [UpdatedAtUtc] DATETIME2 NOT NULL CONSTRAINT [UserDirectoryEntry_UpdatedAtUtc_df] DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT [UserDirectoryEntry_pkey] PRIMARY KEY CLUSTERED ([UserId])
);

-- CreateIndex
CREATE NONCLUSTERED INDEX [UserDirectoryEntry_TenantId_IsActive_idx] ON [dbo].[UserDirectoryEntry]([TenantId], [IsActive]);

COMMIT TRAN;

END TRY
BEGIN CATCH

IF @@TRANCOUNT > 0
BEGIN
    ROLLBACK TRAN;
END;
THROW

END CATCH
