BEGIN TRY

BEGIN TRAN;

-- D-71: varias grabaciones por meeting/call, numeradas por Sequence.
ALTER TABLE [dbo].[RecordingSession] DROP CONSTRAINT [RecordingSession_Scope_ScopeId_key];
ALTER TABLE [dbo].[RecordingSession] ADD [Sequence] INT NOT NULL CONSTRAINT [RecordingSession_Sequence_df] DEFAULT 1;
ALTER TABLE [dbo].[RecordingSession] ADD CONSTRAINT [RecordingSession_Scope_ScopeId_Sequence_key] UNIQUE NONCLUSTERED ([Scope], [ScopeId], [Sequence]);

-- Un transcript por grabacion (las notas de voz, sin sesion, siguen siendo uno por mensaje).
ALTER TABLE [dbo].[TranscriptArtifact] DROP CONSTRAINT [TranscriptArtifact_Scope_ScopeId_key];
ALTER TABLE [dbo].[TranscriptArtifact] ADD CONSTRAINT [TranscriptArtifact_Scope_ScopeId_RecordingSessionId_key] UNIQUE NONCLUSTERED ([Scope], [ScopeId], [RecordingSessionId]);

COMMIT TRAN;

END TRY
BEGIN CATCH

IF @@TRANCOUNT > 0
BEGIN
    ROLLBACK TRAN;
END;
THROW

END CATCH
