import type { TranscriptArtifact, TranscriptScope } from '../../domain/recording/transcript-artifact.js';

/**
 * F7 (T-34): un transcript por grabacion (D-71) y uno por nota de voz; save() es upsert por Id.
 */
export interface TranscriptArtifactRepository {
  save(artifact: TranscriptArtifact): Promise<void>;
  /** Notas de voz (y transcripts anteriores a D-71, sin sesion). */
  findByScope(tenantId: string, scope: TranscriptScope, scopeId: string): Promise<TranscriptArtifact | null>;
  /** El transcript de una grabacion de meeting/call. */
  findByRecordingSession(tenantId: string, recordingSessionId: string): Promise<TranscriptArtifact | null>;
  /** MNT-01 (E-292): el transcript cuyo .txt o .json es ese archivo de CloudStorage. */
  findByFileId?(tenantId: string, fileId: string): Promise<TranscriptArtifact | null>;
  /** Ready con archivos sin confirmar, terminados antes de `completedBefore` (los mas viejos primero). */
  listReadyUnconfirmed?(completedBefore: Date, take: number): Promise<TranscriptArtifact[]>;
}
