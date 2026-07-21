BEGIN TRY

BEGIN TRAN;

-- CreateTable
CREATE TABLE [dbo].[CustomerPreparerAssignment] (
    [CustomerId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [PreparerUserId] UNIQUEIDENTIFIER NOT NULL,
    [AssignedAtUtc] DATETIME2 NOT NULL CONSTRAINT [CustomerPreparerAssignment_AssignedAtUtc_df] DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT [CustomerPreparerAssignment_pkey] PRIMARY KEY CLUSTERED ([CustomerId])
);

-- CreateIndex
CREATE NONCLUSTERED INDEX [CustomerPreparerAssignment_TenantId_PreparerUserId_idx] ON [dbo].[CustomerPreparerAssignment]([TenantId], [PreparerUserId]);

COMMIT TRAN;

END TRY
BEGIN CATCH

IF @@TRANCOUNT > 0
BEGIN
    ROLLBACK TRAN;
END;
THROW

END CATCH
