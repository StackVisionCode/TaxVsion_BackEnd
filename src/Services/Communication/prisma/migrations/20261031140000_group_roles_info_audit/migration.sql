-- F8b: grupos (roles, datos del grupo, modo anuncio, cliente del participante, auditoría y D-20).

ALTER TABLE [dbo].[TenantCommunicationSettings] ADD [GroupsAllowMultipleClients] BIT NOT NULL
  CONSTRAINT [TenantCommunicationSettings_GroupsAllowMultipleClients_df] DEFAULT 0;

ALTER TABLE [dbo].[Conversation] ADD
  [Description] NVARCHAR(500) NULL,
  [AvatarFileId] UNIQUEIDENTIFIER NULL,
  [AdminsOnlyWrite] BIT NOT NULL CONSTRAINT [Conversation_AdminsOnlyWrite_df] DEFAULT 0;

ALTER TABLE [dbo].[ConversationParticipant] ADD [CustomerId] UNIQUEIDENTIFIER NULL;

CREATE NONCLUSTERED INDEX [ConversationParticipant_TenantId_CustomerId_idx]
  ON [dbo].[ConversationParticipant]([TenantId], [CustomerId]);

CREATE TABLE [dbo].[ConversationAudit] (
  [Id] UNIQUEIDENTIFIER NOT NULL,
  [TenantId] UNIQUEIDENTIFIER NOT NULL,
  [ConversationId] UNIQUEIDENTIFIER NOT NULL,
  [Action] NVARCHAR(48) NOT NULL,
  [ActorUserId] UNIQUEIDENTIFIER NULL,
  [TargetUserId] UNIQUEIDENTIFIER NULL,
  [Metadata] NVARCHAR(2000) NULL,
  [OccurredAtUtc] DATETIME2 NOT NULL CONSTRAINT [ConversationAudit_OccurredAtUtc_df] DEFAULT CURRENT_TIMESTAMP,
  CONSTRAINT [ConversationAudit_pkey] PRIMARY KEY CLUSTERED ([Id])
);

CREATE NONCLUSTERED INDEX [ConversationAudit_TenantId_ConversationId_OccurredAtUtc_idx]
  ON [dbo].[ConversationAudit]([TenantId], [ConversationId], [OccurredAtUtc] DESC);

-- D-58: cliente de cada participante de portal (columna nueva en esta migración → sp_executesql).
EXEC sp_executesql N'
UPDATE p SET p.[CustomerId] = a.[CustomerId]
FROM [dbo].[ConversationParticipant] p
JOIN [dbo].[CustomerPortalAccount] a ON a.[UserId] = p.[UserId] AND a.[TenantId] = p.[TenantId]
WHERE p.[ActorType] = ''CustomerPortal'' AND p.[CustomerId] IS NULL;';
