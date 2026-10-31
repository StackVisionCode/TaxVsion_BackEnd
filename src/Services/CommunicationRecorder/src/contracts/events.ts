import type { FailureReason } from '../spool/manifest.js';

export const RECORDING_COMPOSED_EVENT = 'communication.recording.composed.v1';
export const COMPOSITION_FAILED_EVENT = 'communication.recording.composition_failed.v1';

/** Sin actor humano: mismo valor reservado que usa el transcript worker. */
export const SYSTEM_ACTOR_ID = '00000000-0000-0000-0000-000000000000';
export const REQUESTING_SERVICE = 'communication-recorder';

interface Envelope {
  readonly eventId: string;
  readonly eventType: string;
  readonly tenantId: string;
  readonly correlationId: string;
  readonly occurredOnUtc: string;
}

export interface ComposedAudioTrack {
  readonly trackId: string;
  readonly userId: string;
  readonly fileId: string;
  readonly source: 'camera' | 'screen';
  readonly startOffsetMs: number;
  readonly durationMs: number;
}

export interface RecordingComposedEvent extends Envelope {
  readonly sessionId: string;
  readonly scope: 'Meeting' | 'Call';
  readonly scopeId: string;
  readonly composedFileId: string;
  readonly composedSizeBytes: number;
  readonly format: 'mp4' | 'webm';
  readonly durationSeconds: number;
  readonly layout: 'auto';
  readonly audioTracks: readonly ComposedAudioTrack[];
  readonly videoTrackCount: number;
  readonly composedAtUtc: string;
}

export interface CompositionFailedEvent extends Envelope {
  readonly sessionId: string;
  readonly scope: 'Meeting' | 'Call';
  readonly scopeId: string;
  readonly reason: FailureReason;
  readonly errorMessage: string;
}

interface SessionRef {
  readonly sessionId: string;
  readonly tenantId: string;
  readonly scope: 'Meeting' | 'Call';
  readonly scopeId: string;
  readonly correlationId: string;
}

export function buildComposedEvent(
  session: SessionRef,
  data: {
    eventId: string;
    occurredOnUtc: string;
    composedFileId: string;
    composedSizeBytes: number;
    format: 'mp4' | 'webm';
    durationSeconds: number;
    audioTracks: readonly ComposedAudioTrack[];
    videoTrackCount: number;
    composedAtUtc: string;
  },
): RecordingComposedEvent {
  return {
    eventId: data.eventId,
    eventType: RECORDING_COMPOSED_EVENT,
    tenantId: session.tenantId,
    correlationId: session.correlationId,
    occurredOnUtc: data.occurredOnUtc,
    sessionId: session.sessionId,
    scope: session.scope,
    scopeId: session.scopeId,
    composedFileId: data.composedFileId,
    composedSizeBytes: data.composedSizeBytes,
    format: data.format,
    durationSeconds: data.durationSeconds,
    layout: 'auto',
    audioTracks: data.audioTracks,
    videoTrackCount: data.videoTrackCount,
    composedAtUtc: data.composedAtUtc,
  };
}

export function buildCompositionFailedEvent(
  session: SessionRef,
  data: { eventId: string; occurredOnUtc: string; reason: FailureReason; errorMessage: string },
): CompositionFailedEvent {
  return {
    eventId: data.eventId,
    eventType: COMPOSITION_FAILED_EVENT,
    tenantId: session.tenantId,
    correlationId: session.correlationId,
    occurredOnUtc: data.occurredOnUtc,
    sessionId: session.sessionId,
    scope: session.scope,
    scopeId: session.scopeId,
    reason: data.reason,
    errorMessage: data.errorMessage.slice(0, 2000),
  };
}

export type FolderType = 'Recordings' | 'RecordingTracks';

export interface SaveFileRequestedInput {
  readonly eventId: string;
  readonly occurredOn: string;
  readonly tenantId: string;
  readonly correlationId: string;
  readonly fileId: string;
  readonly sourceBucket: string;
  readonly sourceObjectKey: string;
  readonly ownerId: string;
  readonly folderType: FolderType;
  readonly originalName: string;
  readonly contentType: string;
  readonly sizeBytes: number;
}

/**
 * Cuerpo de `SaveFileRequestedIntegrationEvent` en PascalCase exacto (mismo contrato que el transcript
 * worker y Signature) para la cola dedicada de CloudStorage.
 */
export function buildSaveFileRequested(input: SaveFileRequestedInput): Record<string, unknown> {
  return {
    EventId: input.eventId,
    TenantId: input.tenantId,
    OccurredOn: input.occurredOn,
    CorrelationId: input.correlationId,
    FileId: input.fileId,
    RequestingService: REQUESTING_SERVICE,
    SourceBucket: input.sourceBucket,
    SourceObjectKey: input.sourceObjectKey,
    ActorId: SYSTEM_ACTOR_ID,
    OwnerType: 'Communication',
    OwnerId: input.ownerId,
    FolderType: input.folderType,
    TaxYear: null,
    OriginalName: input.originalName,
    ContentType: input.contentType,
    SizeBytes: input.sizeBytes,
  };
}

/** `Meeting_Recording_2026-10-30.mp4` / `Call_Recording_…` (§9.2). */
export function composedFileName(scope: 'Meeting' | 'Call', startedAtUtc: string, format: 'mp4' | 'webm'): string {
  return `${scope}_Recording_${new Date(startedAtUtc).toISOString().slice(0, 10)}.${format}`;
}

export function trackFileName(userId: string, trackId: string): string {
  return `Track_${userId}_${trackId}.webm`;
}
