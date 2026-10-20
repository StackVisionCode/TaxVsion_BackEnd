BEGIN TRY

BEGIN TRAN;

-- AlterTable
ALTER TABLE [dbo].[NotificationEntry] ADD [Action] NVARCHAR(32),
[ActorDisplayName] NVARCHAR(160),
[ActorUserId] UNIQUEIDENTIFIER,
[BodyKey] NVARCHAR(160),
[Category] NVARCHAR(32),
[GroupKey] NVARCHAR(256),
[OccurredAtUtc] DATETIME2,
[ParamsJson] NVARCHAR(max),
[ResourceContextJson] NVARCHAR(max),
[ResourceId] NVARCHAR(128),
[ResourceType] NVARCHAR(64),
[TitleKey] NVARCHAR(160);

-- CreateIndex
CREATE NONCLUSTERED INDEX [NotificationEntry_TenantId_UserId_GroupKey_ReadAtUtc_idx] ON [dbo].[NotificationEntry]([TenantId], [UserId], [GroupKey], [ReadAtUtc]);

COMMIT TRAN;

END TRY
BEGIN CATCH

IF @@TRANCOUNT > 0
BEGIN
    ROLLBACK TRAN;
END;
THROW

END CATCH
