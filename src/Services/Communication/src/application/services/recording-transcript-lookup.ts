import type { RecordingSession } from '../../domain/recording/recording-session.js';
import type { TranscriptArtifact } from '../../domain/recording/transcript-artifact.js';
import type { TranscriptArtifactRepository } from '../ports/transcript-artifact-repository.js';

/**
 * El transcript de una grabacion (D-71). Los anteriores a D-71 no siempre guardaban la sesion: si es
 * la primera grabacion del meeting/call, se acepta el del scope.
 */
export async function findRecordingTranscript(
  transcripts: Pick<TranscriptArtifactRepository, 'findByRecordingSession' | 'findByScope'>,
  session: RecordingSession,
): Promise<TranscriptArtifact | null> {
  const tenantId = session.toSnapshot().tenantId;
  const own = await transcripts.findByRecordingSession(tenantId, session.id);
  if (own) return own;
  if (session.sequence !== 1) return null;
  const legacy = await transcripts.findByScope(tenantId, session.scope, session.scopeId);
  return legacy && legacy.toSnapshot().recordingSessionId === null ? legacy : null;
}
