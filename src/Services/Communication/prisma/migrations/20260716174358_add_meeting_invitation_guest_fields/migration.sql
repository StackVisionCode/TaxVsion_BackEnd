BEGIN TRY

BEGIN TRAN;

-- AlterTable
ALTER TABLE [dbo].[MeetingInvitation] ADD [InviteeExternalPhone] NVARCHAR(32),
[InviteeKind] NVARCHAR(16) NOT NULL CONSTRAINT [MeetingInvitation_InviteeKind_df] DEFAULT 'External',
[InviteeName] NVARCHAR(120);

-- AlterTable
ALTER TABLE [dbo].[MeetingParticipant] ADD [ActorType] NVARCHAR(32) NOT NULL CONSTRAINT [MeetingParticipant_ActorType_df] DEFAULT 'TenantEmployee';

COMMIT TRAN;

END TRY
BEGIN CATCH

IF @@TRANCOUNT > 0
BEGIN
    ROLLBACK TRAN;
END;
THROW

END CATCH
