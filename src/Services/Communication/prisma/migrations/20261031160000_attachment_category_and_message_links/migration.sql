-- F8d: categoría y datos reales de los adjuntos (ADD-01) y enlaces de los mensajes (T-16).
-- El relleno de lo existente (T-17) va en un job aparte e idempotente: `npm run job:backfill-chat-content`.

ALTER TABLE [dbo].[AttachmentTracking] ADD
  [Category] NVARCHAR(16) NULL,
  [ContentType] NVARCHAR(128) NULL,
  [FileName] NVARCHAR(260) NULL,
  [SizeBytes] BIGINT NULL;

CREATE NONCLUSTERED INDEX [AttachmentTracking_ConversationId_Category_CreatedAtUtc_idx]
  ON [dbo].[AttachmentTracking]([ConversationId], [Category], [CreatedAtUtc] DESC);

CREATE TABLE [dbo].[MessageLink] (
  [Id] UNIQUEIDENTIFIER NOT NULL,
  [TenantId] UNIQUEIDENTIFIER NOT NULL,
  [ConversationId] UNIQUEIDENTIFIER NOT NULL,
  [MessageId] UNIQUEIDENTIFIER NOT NULL,
  [Url] NVARCHAR(2048) NOT NULL,
  [Domain] NVARCHAR(255) NOT NULL,
  [CreatedAtUtc] DATETIME2 NOT NULL,
  CONSTRAINT [MessageLink_pkey] PRIMARY KEY CLUSTERED ([Id]),
  CONSTRAINT [MessageLink_MessageId_fkey] FOREIGN KEY ([MessageId]) REFERENCES [dbo].[Message]([Id]) ON DELETE CASCADE ON UPDATE CASCADE
);

CREATE NONCLUSTERED INDEX [MessageLink_ConversationId_CreatedAtUtc_idx]
  ON [dbo].[MessageLink]([ConversationId], [CreatedAtUtc] DESC);

CREATE NONCLUSTERED INDEX [MessageLink_MessageId_idx]
  ON [dbo].[MessageLink]([MessageId]);
