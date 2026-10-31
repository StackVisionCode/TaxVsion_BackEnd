import type { RecordingCompositionPolicy } from '../../domain/settings/effective-communication-settings.js';

/**
 * F7 (D-28): canal de control hacia `communication-recorder` (contrato 9.1 del spec de F7). El recorder
 * no conoce reglas de negocio: Communication le dice que grabar y le pasa el timeline ya resuelto.
 */
export interface RecorderSessionStart {
  readonly sessionId: string;
  readonly tenantId: string;
  readonly scope: 'Meeting' | 'Call';
  readonly scopeId: string;
  readonly startedAtUtc: string;
  readonly maxDurationSeconds: number;
  readonly toleranceSeconds: number;
  readonly tracksRetentionDays: number;
  readonly captureVideo: boolean;
  readonly composition: Omit<RecordingCompositionPolicy, 'captureSpatialLayer'>;
}

export interface RecorderTrackStart {
  readonly trackId: string;
  readonly producerId: string;
  readonly userId: string;
  readonly kind: 'audio' | 'video';
  readonly source: 'camera' | 'screen';
  readonly codec: { readonly mimeType: string; readonly payloadType: number; readonly clockRate: number; readonly channels?: number };
  readonly ssrc: number;
}

export interface RecorderTrackEndpoint {
  /** null = Communication resuelve el host del recorder. */
  readonly ip: string | null;
  readonly port: number;
  readonly rtcpPort: number;
}

export interface RecorderTimelineParticipant {
  readonly userId: string;
  readonly displayName: string;
  readonly role: 'Host' | 'Cohost' | 'Attendee';
  readonly actorType: string | null;
  /** Foto de perfil (CloudStorage `Avatars`); el recorder la pinta con la cámara apagada. null = iniciales. */
  readonly avatarFileId: string | null;
}

export interface RecorderTimelineEvent {
  readonly seq: number;
  readonly at: string;
  readonly type: string;
  readonly actorUserId: string | null;
  readonly targetUserId: string | null;
  readonly payload: Readonly<Record<string, unknown>> | null;
}

export interface RecorderTimeline {
  readonly participants: readonly RecorderTimelineParticipant[];
  readonly events: readonly RecorderTimelineEvent[];
}

/** Error de negocio del recorder (`{code}` del contrato); cualquier otro fallo se lanza tal cual. */
export class RecorderRequestError extends Error {
  constructor(
    public readonly code: string,
    public readonly status: number,
    message: string,
  ) {
    super(message);
    this.name = 'RecorderRequestError';
  }
}

export interface RecorderClient {
  /** false cuando no hay URL configurada (la captura server-side no esta disponible en este despliegue). */
  readonly configured: boolean;
  startSession(tenantId: string, input: RecorderSessionStart): Promise<void>;
  addTrack(tenantId: string, sessionId: string, input: RecorderTrackStart): Promise<RecorderTrackEndpoint>;
  closeTrack(tenantId: string, sessionId: string, trackId: string): Promise<void>;
  stopSession(tenantId: string, sessionId: string, input: { stoppedAtUtc: string; reason: string; timeline: RecorderTimeline }): Promise<void>;
  recompose(
    tenantId: string,
    sessionId: string,
    input: { timeline: RecorderTimeline; composition: Omit<RecordingCompositionPolicy, 'captureSpatialLayer'> },
  ): Promise<void>;
  abortSession(tenantId: string, sessionId: string): Promise<void>;
  /** Host del recorder resuelto a IP (para conectar el PlainTransport cuando el recorder no anuncia una). */
  resolveHostAddress(): Promise<string>;
}
