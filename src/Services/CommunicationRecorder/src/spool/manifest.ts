import type { CompositionSettings, TrackCodec } from '../contracts/api.js';

export type SessionState =
  | 'capturing'
  | 'limit_reached'
  | 'queued'
  | 'composing'
  | 'composed'
  | 'failed'
  | 'aborted';

export type FailureReason = 'NoTracks' | 'ComposeError' | 'UploadFailed' | 'RecorderRestarted' | 'Aborted';

export interface ManifestTrack {
  readonly trackId: string;
  readonly producerId: string;
  readonly userId: string;
  readonly kind: 'audio' | 'video';
  readonly source: 'camera' | 'screen';
  readonly codec: TrackCodec;
  readonly ssrc: number;
  /** Nombre del archivo dentro de la carpeta de la sesión. */
  readonly file: string;
  readonly rtpPort: number;
  readonly rtcpPort: number;
  /** Instante de apertura de la pista menos `startedAtUtc` de la sesión. */
  readonly startOffsetMs: number;
  endedOffsetMs: number | null;
  /** FileId de CloudStorage de la pista de audio ya subida (se reutiliza al recomponer). */
  fileId?: string;
  durationMs?: number;
}

export interface CompositionResult {
  readonly jobId: string;
  readonly composedFileId: string;
  readonly composedSizeBytes: number;
  readonly format: 'mp4' | 'webm';
  readonly durationSeconds: number;
  readonly composedAtUtc: string;
  readonly videoTrackCount: number;
}

export interface SessionManifest {
  readonly version: 1;
  readonly sessionId: string;
  readonly tenantId: string;
  readonly scope: 'Meeting' | 'Call';
  readonly scopeId: string;
  readonly startedAtUtc: string;
  readonly maxDurationSeconds: number;
  readonly toleranceSeconds: number;
  readonly tracksRetentionDays: number;
  readonly captureVideo: boolean;
  composition: CompositionSettings;
  readonly correlationId: string;
  readonly createdAtUtc: string;
  state: SessionState;
  tracks: ManifestTrack[];
  hardStoppedAtUtc?: string;
  stop?: { stoppedAtUtc: string | null; reason: string; receivedAtUtc: string };
  jobId?: string;
  failure?: { reason: FailureReason; message: string; atUtc: string };
  result?: CompositionResult;
  /** A partir de aquí el janitor puede borrar la carpeta (pistas + manifest). */
  retainUntilUtc?: string;
  updatedAtUtc: string;
}

export const ACTIVE_CAPTURE_STATES: readonly SessionState[] = ['capturing', 'limit_reached'];
