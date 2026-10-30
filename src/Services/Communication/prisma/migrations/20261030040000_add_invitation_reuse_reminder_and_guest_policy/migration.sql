BEGIN TRY

BEGIN TRAN;

-- AlterTable: F6 (T-64, D-47) recordatorio T-N y sala de espera por defecto para guests (plataforma).
ALTER TABLE [dbo].[PlatformCommunicationSettings] ADD
    [MeetingReminderMinutesBefore] INT NOT NULL CONSTRAINT [PlatformCommunicationSettings_MeetingReminderMinutesBefore_df] DEFAULT 15,
    [GuestsWaitingRoomDefault] BIT NOT NULL CONSTRAINT [PlatformCommunicationSettings_GuestsWaitingRoomDefault_df] DEFAULT 1;

-- AlterTable: F6 overrides de oficina (NULL = hereda la plataforma).
ALTER TABLE [dbo].[TenantCommunicationSettings] ADD
    [MeetingReminderMinutesBefore] INT NULL,
    [GuestsWaitingRoom] BIT NULL;

-- AlterTable: F6 (T-64) el recordatorio se envia una sola vez.
ALTER TABLE [dbo].[Meeting] ADD
    [ReminderSentAtUtc] DATETIME2 NULL;

-- AlterTable: F6 (D-48) token reutilizable para usuarios con cuenta (auditoria de usos) y cierre por la reunion.
ALTER TABLE [dbo].[MeetingInvitation] ADD
    [FirstUsedAtUtc] DATETIME2 NULL,
    [LastUsedAtUtc] DATETIME2 NULL,
    [UseCount] INT NOT NULL CONSTRAINT [MeetingInvitation_UseCount_df] DEFAULT 0,
    [ClosedAtUtc] DATETIME2 NULL;

-- Las invitaciones de usuarios con cuenta ya usadas una vez siguen valiendo (D-48): el uso pasa a la auditoria.
-- sp_executesql: las columnas recien agregadas no existen al compilar el lote; se difiere la compilacion.
EXEC sp_executesql N'UPDATE [dbo].[MeetingInvitation]
SET [FirstUsedAtUtc] = [UsedAtUtc], [LastUsedAtUtc] = [UsedAtUtc], [UseCount] = 1, [UsedAtUtc] = NULL
WHERE [UsedAtUtc] IS NOT NULL AND [InviteeKind] <> N''External''';

-- La fila global sube de version para que las caches por oficina recalculen.
UPDATE [dbo].[PlatformCommunicationSettings] SET [Version] = [Version] + 1 WHERE [Id] = N'global';

COMMIT TRAN;

END TRY
BEGIN CATCH

IF @@TRANCOUNT > 0
BEGIN
    ROLLBACK TRAN;
END;
THROW

END CATCH
