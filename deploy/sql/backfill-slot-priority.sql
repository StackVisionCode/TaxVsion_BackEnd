SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;

BEGIN TRANSACTION;

UPDATE [dbo].[Users]
SET [SlotPriorityAt] = NULL
WHERE ([IsActive] = 0 OR [ActorType] IN (N'CustomerPortal', N'PlatformAdmin'))
  AND [SlotPriorityAt] IS NOT NULL;

UPDATE [dbo].[Users]
SET [SlotPriorityAt] = '0001-01-01T00:00:00.0000000'
WHERE [IsActive] = 1
  AND [ActorType] = N'TenantAdmin'
  AND [OnboardingId] IS NOT NULL
  AND [SlotPriorityAt] <> '0001-01-01T00:00:00.0000000';

;WITH [LegacyOwners] AS
(
    SELECT
        [u].[Id],
        ROW_NUMBER() OVER (PARTITION BY [u].[TenantId] ORDER BY [u].[CreatedAtUtc], [u].[Id]) AS [RowNumber]
    FROM [dbo].[Users] AS [u]
    WHERE [u].[IsActive] = 1
      AND [u].[ActorType] = N'TenantAdmin'
      AND NOT EXISTS
      (
          SELECT 1
          FROM [dbo].[Users] AS [owner]
          WHERE [owner].[TenantId] = [u].[TenantId]
            AND [owner].[IsActive] = 1
            AND [owner].[ActorType] = N'TenantAdmin'
            AND [owner].[SlotPriorityAt] = '0001-01-01T00:00:00.0000000'
      )
)
UPDATE [u]
SET [SlotPriorityAt] = '0001-01-01T00:00:00.0000000'
FROM [dbo].[Users] AS [u]
INNER JOIN [LegacyOwners] AS [owner] ON [owner].[Id] = [u].[Id]
WHERE [owner].[RowNumber] = 1;

UPDATE [dbo].[Users]
SET [SlotPriorityAt] = COALESCE([CreatedAtUtc], SYSUTCDATETIME())
WHERE [IsActive] = 1
  AND [ActorType] IN (N'TenantEmployee', N'TenantAdmin')
  AND [SlotPriorityAt] IS NULL;

SELECT [Id] AS [TenantId], [MaxUsers]
INTO #TenantScope
FROM [dbo].[TenantPlanLimits]
WHERE [MaxUsers] > 0;

;WITH [Ranked] AS
(
    SELECT
        [u].[TenantId],
        [u].[Id] AS [UserId],
        [scope].[MaxUsers] AS [EffectiveCap],
        CASE
            WHEN [u].[ActorType] = N'TenantAdmin'
             AND [u].[SlotPriorityAt] = '0001-01-01T00:00:00.0000000'
            THEN CAST(1 AS bit)
            ELSE CAST(0 AS bit)
        END AS [IsOwner],
        ROW_NUMBER() OVER
        (
            PARTITION BY [u].[TenantId]
            ORDER BY
                CASE
                    WHEN [u].[ActorType] = N'TenantAdmin'
                     AND [u].[SlotPriorityAt] = '0001-01-01T00:00:00.0000000'
                    THEN 0 ELSE 1
                END,
                [u].[SlotPriorityAt],
                [u].[Id]
        ) AS [SlotIndex]
    FROM [dbo].[Users] AS [u]
    INNER JOIN #TenantScope AS [scope] ON [scope].[TenantId] = [u].[TenantId]
    WHERE [u].[IsActive] = 1
      AND [u].[ActorType] IN (N'TenantEmployee', N'TenantAdmin')
)
SELECT
    [TenantId],
    [UserId],
    CONVERT(int, [SlotIndex]) AS [SlotIndex],
    [EffectiveCap],
    CASE WHEN [IsOwner] = 0 AND [SlotIndex] > [EffectiveCap] THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END AS [IsGated]
INTO #Desired
FROM [Ranked];

SELECT DISTINCT
    COALESCE([desired].[TenantId], [current].[TenantId]) AS [TenantId],
    CAST(1 AS bigint) AS [NextRevision]
INTO #ChangedTenants
FROM #Desired AS [desired]
FULL OUTER JOIN
(
    SELECT [projection].*
    FROM [dbo].[TenantSeatSlotProjections] AS [projection]
    INNER JOIN #TenantScope AS [scope] ON [scope].[TenantId] = [projection].[TenantId]
) AS [current]
    ON [current].[TenantId] = [desired].[TenantId]
   AND [current].[UserId] = [desired].[UserId]
WHERE [desired].[UserId] IS NULL
   OR [current].[UserId] IS NULL
   OR [current].[SlotIndex] <> [desired].[SlotIndex]
   OR [current].[EffectiveCap] <> [desired].[EffectiveCap]
   OR [current].[IsGated] <> [desired].[IsGated];

UPDATE [changed]
SET [NextRevision] = COALESCE
(
    (
        SELECT MAX([projection].[RevisionNumber]) + 1
        FROM [dbo].[TenantSeatSlotProjections] AS [projection]
        WHERE [projection].[TenantId] = [changed].[TenantId]
    ),
    1
)
FROM #ChangedTenants AS [changed];

DELETE [projection]
FROM [dbo].[TenantSeatSlotProjections] AS [projection]
INNER JOIN #ChangedTenants AS [changed] ON [changed].[TenantId] = [projection].[TenantId];

INSERT INTO [dbo].[TenantSeatSlotProjections]
(
    [TenantId],
    [UserId],
    [SlotIndex],
    [EffectiveCap],
    [IsGated],
    [ComputedAtUtc],
    [RevisionNumber]
)
SELECT
    [desired].[TenantId],
    [desired].[UserId],
    [desired].[SlotIndex],
    [desired].[EffectiveCap],
    [desired].[IsGated],
    SYSUTCDATETIME(),
    [changed].[NextRevision]
FROM #Desired AS [desired]
INNER JOIN #ChangedTenants AS [changed] ON [changed].[TenantId] = [desired].[TenantId];

SELECT COUNT(*) AS [ChangedTenantCount]
FROM #ChangedTenants;

COMMIT TRANSACTION;
