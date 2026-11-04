BEGIN TRY

BEGIN TRAN;

-- Retiro de un empleado: revocar sus invitaciones abiertas y contarlas en el pre-flight sin recorrer la tabla.
CREATE NONCLUSTERED INDEX [MeetingInvitation_TenantId_InviteeUserId_idx] ON [dbo].[MeetingInvitation]([TenantId], [InviteeUserId]);

COMMIT TRAN;

END TRY
BEGIN CATCH

IF @@TRANCOUNT > 0
BEGIN
    ROLLBACK TRAN;
END;
THROW

END CATCH
