BEGIN TRY

BEGIN TRAN;

-- AlterTable: F8 techo de la ventana de edición por tipo de chat (0 = no se edita) y tope de ediciones.
ALTER TABLE [dbo].[PlatformCommunicationSettings] ADD
    [ChatEditWindowMinutesDirect] INT NOT NULL CONSTRAINT [PlatformCommunicationSettings_ChatEditWindowMinutesDirect_df] DEFAULT 15,
    [ChatEditWindowMinutesGroup] INT NOT NULL CONSTRAINT [PlatformCommunicationSettings_ChatEditWindowMinutesGroup_df] DEFAULT 15,
    [ChatEditWindowMinutesSupport] INT NOT NULL CONSTRAINT [PlatformCommunicationSettings_ChatEditWindowMinutesSupport_df] DEFAULT 5,
    [ChatEditWindowMinutesMeeting] INT NOT NULL CONSTRAINT [PlatformCommunicationSettings_ChatEditWindowMinutesMeeting_df] DEFAULT 15,
    [ChatMaxEditsPerMessage] INT NOT NULL CONSTRAINT [PlatformCommunicationSettings_ChatMaxEditsPerMessage_df] DEFAULT 5;

-- AlterTable: F8 override de la oficina (NULL = hereda la plataforma).
ALTER TABLE [dbo].[TenantCommunicationSettings] ADD
    [ChatEditWindowMinutesDirect] INT NULL,
    [ChatEditWindowMinutesGroup] INT NULL,
    [ChatEditWindowMinutesSupport] INT NULL,
    [ChatEditWindowMinutesMeeting] INT NULL,
    [ChatMaxEditsPerMessage] INT NULL;

-- AlterTable: F8 cuántas veces se editó cada mensaje.
ALTER TABLE [dbo].[Message] ADD
    [EditCount] INT NOT NULL CONSTRAINT [Message_EditCount_df] DEFAULT 0;

-- CreateTable: F8 historial de ediciones (texto anterior a cada edición).
CREATE TABLE [dbo].[MessageEdit] (
    [Id] UNIQUEIDENTIFIER NOT NULL,
    [MessageId] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [Revision] INT NOT NULL,
    [PreviousBody] NVARCHAR(4000) NOT NULL,
    [EditedByUserId] UNIQUEIDENTIFIER NOT NULL,
    [EditedAtUtc] DATETIME2 NOT NULL,
    CONSTRAINT [MessageEdit_pkey] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [MessageEdit_MessageId_Revision_key] UNIQUE NONCLUSTERED ([MessageId],[Revision])
);

-- CreateIndex
CREATE NONCLUSTERED INDEX [MessageEdit_TenantId_MessageId_idx] ON [dbo].[MessageEdit]([TenantId], [MessageId]);

-- AddForeignKey
ALTER TABLE [dbo].[MessageEdit] ADD CONSTRAINT [MessageEdit_MessageId_fkey] FOREIGN KEY ([MessageId]) REFERENCES [dbo].[Message]([Id]) ON DELETE CASCADE ON UPDATE CASCADE;

COMMIT TRAN;

END TRY
BEGIN CATCH

IF @@TRANCOUNT > 0
BEGIN
    ROLLBACK TRAN;
END;
THROW

END CATCH
