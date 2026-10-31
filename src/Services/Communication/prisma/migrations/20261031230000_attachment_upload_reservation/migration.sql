-- F11 (seguridad, E-259): reserva de la subida de un adjunto; send-message la exige y la consume.
CREATE TABLE [dbo].[AttachmentUploadReservation] (
    [FileId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [ConversationId] UNIQUEIDENTIFIER NOT NULL,
    [UploadedByUserId] UNIQUEIDENTIFIER NOT NULL,
    [CreatedAtUtc] DATETIME2 NOT NULL CONSTRAINT [AttachmentUploadReservation_CreatedAtUtc_df] DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT [AttachmentUploadReservation_pkey] PRIMARY KEY CLUSTERED ([FileId])
);

CREATE NONCLUSTERED INDEX [AttachmentUploadReservation_CreatedAtUtc_idx] ON [dbo].[AttachmentUploadReservation]([CreatedAtUtc]);
