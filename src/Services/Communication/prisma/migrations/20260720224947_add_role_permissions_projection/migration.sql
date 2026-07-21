BEGIN TRY

BEGIN TRAN;

-- AlterTable
ALTER TABLE [dbo].[UserPermissionsProjection] ADD [RoleIds] NVARCHAR(max) NOT NULL CONSTRAINT [UserPermissionsProjection_RoleIds_df] DEFAULT '[]';

-- CreateTable
CREATE TABLE [dbo].[RolePermissionsProjection] (
    [RoleId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [RoleName] NVARCHAR(60) NOT NULL,
    [PermissionCodes] NVARCHAR(max) NOT NULL,
    [PermissionsVersion] INT NOT NULL CONSTRAINT [RolePermissionsProjection_PermissionsVersion_df] DEFAULT 1,
    [UpdatedAtUtc] DATETIME2 NOT NULL CONSTRAINT [RolePermissionsProjection_UpdatedAtUtc_df] DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT [RolePermissionsProjection_pkey] PRIMARY KEY CLUSTERED ([RoleId])
);

-- CreateIndex
CREATE NONCLUSTERED INDEX [RolePermissionsProjection_TenantId_idx] ON [dbo].[RolePermissionsProjection]([TenantId]);

COMMIT TRAN;

END TRY
BEGIN CATCH

IF @@TRANCOUNT > 0
BEGIN
    ROLLBACK TRAN;
END;
THROW

END CATCH
