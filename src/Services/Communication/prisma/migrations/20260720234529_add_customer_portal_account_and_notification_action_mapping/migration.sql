BEGIN TRY

BEGIN TRAN;

-- CreateTable
CREATE TABLE [dbo].[CustomerPortalAccount] (
    [CustomerId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [IsActive] BIT NOT NULL CONSTRAINT [CustomerPortalAccount_IsActive_df] DEFAULT 1,
    [UpdatedAtUtc] DATETIME2 NOT NULL CONSTRAINT [CustomerPortalAccount_UpdatedAtUtc_df] DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT [CustomerPortalAccount_pkey] PRIMARY KEY CLUSTERED ([CustomerId])
);

-- CreateTable
CREATE TABLE [dbo].[NotificationActionMapping] (
    [Id] UNIQUEIDENTIFIER NOT NULL,
    [EventKey] NVARCHAR(100) NOT NULL,
    [AudienceRole] NVARCHAR(50) NOT NULL,
    [ActionType] NVARCHAR(16) NOT NULL,
    [UrlTemplate] NVARCHAR(500),
    [CreatedAtUtc] DATETIME2 NOT NULL CONSTRAINT [NotificationActionMapping_CreatedAtUtc_df] DEFAULT CURRENT_TIMESTAMP,
    [UpdatedAtUtc] DATETIME2 NOT NULL CONSTRAINT [NotificationActionMapping_UpdatedAtUtc_df] DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT [NotificationActionMapping_pkey] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [NotificationActionMapping_EventKey_AudienceRole_key] UNIQUE NONCLUSTERED ([EventKey],[AudienceRole])
);

-- CreateIndex
CREATE NONCLUSTERED INDEX [CustomerPortalAccount_TenantId_IsActive_idx] ON [dbo].[CustomerPortalAccount]([TenantId], [IsActive]);

COMMIT TRAN;

END TRY
BEGIN CATCH

IF @@TRANCOUNT > 0
BEGIN
    ROLLBACK TRAN;
END;
THROW

END CATCH
