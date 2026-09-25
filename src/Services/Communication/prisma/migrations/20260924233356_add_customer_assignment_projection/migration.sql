BEGIN TRY

BEGIN TRAN;

-- CreateTable
CREATE TABLE [dbo].[CustomerAssignmentProjection] (
    [Id] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [CustomerId] UNIQUEIDENTIFIER NOT NULL,
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [Version] DATETIME2 NOT NULL,
    CONSTRAINT [CustomerAssignmentProjection_pkey] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [CustomerAssignmentProjection_TenantId_CustomerId_UserId_key] UNIQUE NONCLUSTERED ([TenantId],[CustomerId],[UserId])
);

-- CreateIndex
CREATE NONCLUSTERED INDEX [CustomerAssignmentProjection_TenantId_CustomerId_idx] ON [dbo].[CustomerAssignmentProjection]([TenantId], [CustomerId]);

COMMIT TRAN;

END TRY
BEGIN CATCH

IF @@TRANCOUNT > 0
BEGIN
    ROLLBACK TRAN;
END;
THROW

END CATCH
