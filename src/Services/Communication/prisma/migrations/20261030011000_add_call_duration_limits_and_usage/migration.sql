BEGIN TRY

BEGIN TRAN;

-- AlterTable: F4, tope de minutos por llamada (INV-12 H-3).
ALTER TABLE [dbo].[Call] ADD
    [MaxDurationMinutes] INT,
    [EndsAtUtc] DATETIME2,
    [LastDurationWarningSeconds] INT;

-- CreateIndex
CREATE NONCLUSTERED INDEX [Call_Status_EndsAtUtc_idx] ON [dbo].[Call]([Status], [EndsAtUtc]);

-- CreateTable: minutos consumidos por oficina y mes.
CREATE TABLE [dbo].[TenantCallUsage] (
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [YearMonth] NVARCHAR(7) NOT NULL,
    [Seconds] INT NOT NULL CONSTRAINT [TenantCallUsage_Seconds_df] DEFAULT 0,
    [UpdatedAtUtc] DATETIME2 NOT NULL CONSTRAINT [TenantCallUsage_UpdatedAtUtc_df] DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT [TenantCallUsage_pkey] PRIMARY KEY CLUSTERED ([TenantId],[YearMonth])
);

COMMIT TRAN;

END TRY
BEGIN CATCH

IF @@TRANCOUNT > 0
BEGIN
    ROLLBACK TRAN;
END;
THROW

END CATCH
