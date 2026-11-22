BEGIN TRY

BEGIN TRAN;

-- Bandeja: vista "todas" (sin descartadas) y filtro por categoría. Solo índices, sin datos.
CREATE NONCLUSTERED INDEX [NotificationEntry_TenantId_UserId_DismissedAtUtc_CreatedAtUtc_idx] ON [dbo].[NotificationEntry]([TenantId], [UserId], [DismissedAtUtc], [CreatedAtUtc] DESC);

CREATE NONCLUSTERED INDEX [NotificationEntry_TenantId_UserId_Category_CreatedAtUtc_idx] ON [dbo].[NotificationEntry]([TenantId], [UserId], [Category], [CreatedAtUtc] DESC);

COMMIT TRAN;

END TRY
BEGIN CATCH

IF @@TRANCOUNT > 0
BEGIN
    ROLLBACK TRAN;
END;
THROW

END CATCH
