BEGIN TRY

BEGIN TRAN;

-- D-69: la reunion admite invitados externos. D-68: enlace de la reunion para externos.
ALTER TABLE [dbo].[Meeting] ADD
    [GuestsAllowed] BIT NOT NULL CONSTRAINT [Meeting_GuestsAllowed_df] DEFAULT 1,
    [GuestLinkEnabled] BIT NOT NULL CONSTRAINT [Meeting_GuestLinkEnabled_df] DEFAULT 0,
    [GuestLinkVersion] INT NOT NULL CONSTRAINT [Meeting_GuestLinkVersion_df] DEFAULT 0;

-- D-67: un externo siempre espera admision; la opcion de la oficina y de la plataforma se retira.
ALTER TABLE [dbo].[TenantCommunicationSettings] DROP COLUMN [GuestsWaitingRoom];
ALTER TABLE [dbo].[PlatformCommunicationSettings] DROP CONSTRAINT [PlatformCommunicationSettings_GuestsWaitingRoomDefault_df];
ALTER TABLE [dbo].[PlatformCommunicationSettings] DROP COLUMN [GuestsWaitingRoomDefault];

COMMIT TRAN;

END TRY
BEGIN CATCH

IF @@TRANCOUNT > 0
BEGIN
    ROLLBACK TRAN;
END;
THROW

END CATCH
