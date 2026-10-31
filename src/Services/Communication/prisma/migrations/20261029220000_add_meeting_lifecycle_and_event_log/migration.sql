BEGIN TRY

BEGIN TRAN;

-- AlterTable: ciclo de vida de la reunión (INV-11, D-34/D-35) y continuación (D-34).
ALTER TABLE [dbo].[Meeting] ADD
    [ScheduledDurationMinutes] INT,
    [MaxDurationMinutes] INT,
    [EndsAtUtc] DATETIME2,
    [EndReason] NVARCHAR(32),
    [LastDurationWarningSeconds] INT,
    [ContinuationOfMeetingId] UNIQUEIDENTIFIER;

-- AlterTable: D-58, cliente detrás del usuario del portal.
ALTER TABLE [dbo].[MeetingParticipant] ADD [CustomerId] UNIQUEIDENTIFIER;

-- CreateTable
CREATE TABLE [dbo].[MeetingEventLog] (
    [Id] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [Scope] NVARCHAR(16) NOT NULL,
    [ScopeId] UNIQUEIDENTIFIER NOT NULL,
    [Seq] INT NOT NULL,
    [At] DATETIME2 NOT NULL,
    [Type] NVARCHAR(64) NOT NULL,
    [ActorUserId] UNIQUEIDENTIFIER,
    [TargetUserId] UNIQUEIDENTIFIER,
    [PayloadJson] NVARCHAR(max),
    [CorrelationId] NVARCHAR(128),
    CONSTRAINT [MeetingEventLog_pkey] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [MeetingEventLog_ScopeId_Seq_key] UNIQUE NONCLUSTERED ([ScopeId],[Seq])
);

-- CreateIndex
CREATE NONCLUSTERED INDEX [Meeting_TenantId_Status_EndsAtUtc_idx] ON [dbo].[Meeting]([TenantId], [Status], [EndsAtUtc]);

-- CreateIndex
CREATE NONCLUSTERED INDEX [MeetingParticipant_TenantId_CustomerId_idx] ON [dbo].[MeetingParticipant]([TenantId], [CustomerId]);

-- CreateIndex
CREATE NONCLUSTERED INDEX [MeetingEventLog_TenantId_ScopeId_Seq_idx] ON [dbo].[MeetingEventLog]([TenantId], [ScopeId], [Seq]);

-- CreateIndex
CREATE NONCLUSTERED INDEX [MeetingEventLog_TenantId_Type_At_idx] ON [dbo].[MeetingEventLog]([TenantId], [Type], [At]);

COMMIT TRAN;

END TRY
BEGIN CATCH

IF @@TRANCOUNT > 0
BEGIN
    ROLLBACK TRAN;
END;
THROW

END CATCH
