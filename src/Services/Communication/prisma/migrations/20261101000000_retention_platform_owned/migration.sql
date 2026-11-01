-- La retención de mensajes y grabaciones y la purga automática pasan de la oficina a la plataforma:
-- global en PlatformCommunicationSettings y, si hace falta, una excepción por oficina en
-- TenantCommunicationConstraints. Ambas las edita solo PlatformAdmin.
ALTER TABLE [dbo].[PlatformCommunicationSettings] ADD
    [MessageRetentionDays] INT NOT NULL CONSTRAINT [PlatformCommunicationSettings_MessageRetentionDays_df] DEFAULT 365,
    [RecordingRetentionDays] INT NOT NULL CONSTRAINT [PlatformCommunicationSettings_RecordingRetentionDays_df] DEFAULT 90,
    [PurgeEnabled] BIT NOT NULL CONSTRAINT [PlatformCommunicationSettings_PurgeEnabled_df] DEFAULT 0;

ALTER TABLE [dbo].[TenantCommunicationConstraints] ADD
    [MessageRetentionDays] INT NULL,
    [RecordingRetentionDays] INT NULL,
    [PurgeEnabled] BIT NULL;

-- Lo que una oficina ya hubiera cambiado se conserva como su excepción, para que nada se borre ni se
-- guarde distinto de hoy. Columnas recién agregadas en el mismo lote: va por sp_executesql.
EXEC sp_executesql N'
MERGE [dbo].[TenantCommunicationConstraints] AS target
USING (
    SELECT [TenantId], [MessageRetentionDays], [RecordingRetentionDays], [PurgeEnabled]
    FROM [dbo].[TenantCommunicationSettings]
    WHERE [MessageRetentionDays] <> 365 OR [RecordingRetentionDays] <> 90 OR [PurgeEnabled] = 1
) AS source
ON target.[TenantId] = source.[TenantId]
WHEN MATCHED THEN UPDATE SET
    target.[MessageRetentionDays] = source.[MessageRetentionDays],
    target.[RecordingRetentionDays] = source.[RecordingRetentionDays],
    target.[PurgeEnabled] = source.[PurgeEnabled],
    target.[Version] = target.[Version] + 1,
    target.[UpdatedAtUtc] = SYSUTCDATETIME()
WHEN NOT MATCHED THEN INSERT
    ([TenantId], [Version], [MessageRetentionDays], [RecordingRetentionDays], [PurgeEnabled], [CreatedAtUtc], [UpdatedAtUtc])
    VALUES (source.[TenantId], 1, source.[MessageRetentionDays], source.[RecordingRetentionDays], source.[PurgeEnabled], SYSUTCDATETIME(), SYSUTCDATETIME());
';

ALTER TABLE [dbo].[TenantCommunicationSettings] DROP CONSTRAINT [TenantCommunicationSettings_MessageRetentionDays_df];
ALTER TABLE [dbo].[TenantCommunicationSettings] DROP CONSTRAINT [TenantCommunicationSettings_RecordingRetentionDays_df];
ALTER TABLE [dbo].[TenantCommunicationSettings] DROP CONSTRAINT [TenantCommunicationSettings_PurgeEnabled_df];
ALTER TABLE [dbo].[TenantCommunicationSettings] DROP COLUMN [MessageRetentionDays], [RecordingRetentionDays], [PurgeEnabled];
