import type {
  IntegrationEvent,
  RecordingConsentResponse,
  RecordingConsentSnapshotEntry,
} from './integration-event.js';

/**
 * Integration events del ciclo de vida de una llamada REALMENTE publicados
 * hoy por algun use case.
 *
 * NUNCA llevan payload de SDP/ICE/media — solo IDs, timestamps y counters.
 * El bus queda limpio de PII.
 */
export const CallEventTypes = {
  Started:            'communication.call.started.v1',
  Accepted:           'communication.call.accepted.v1',
  Ended:              'communication.call.ended.v1',
  Missed:             'communication.call.missed.v1',
  RecordingConsentRequested: 'communication.call.recording_consent_requested.v1',
  RecordingConsentRecorded: 'communication.call.recording_consent_recorded.v1',
  RecordingStarted:   'communication.call.recording_started.v1',
  RecordingStopped:   'communication.call.recording_stopped.v1',
  RecordingProcessingStarted: 'communication.call.recording_processing_started.v1',
  RecordingReady:     'communication.call.recording_ready.v1',
  TranscriptReady:    'communication.call.transcript_ready.v1',
  UpgradedToVideo:    'communication.call.upgraded_to_video.v1',
  ScreenShareStarted: 'communication.call.screen_share_started.v1',
  ScreenShareStopped: 'communication.call.screen_share_stopped.v1',
  RecordingValidationFailed: 'communication.call.recording_validation_failed.v1',
  RecordingFailed:    'communication.call.recording_failed.v1',
} as const;

export interface CallStartedEvent extends IntegrationEvent {
  readonly eventType: 'communication.call.started.v1';
  readonly callId: string;
  readonly kind: 'Audio' | 'Video';
  readonly callerUserId: string;
  readonly calleeUserId: string;
  readonly conversationId: string | null;
  readonly ringingAtUtc: string;
}

export interface CallAcceptedEvent extends IntegrationEvent {
  readonly eventType: 'communication.call.accepted.v1';
  readonly callId: string;
  readonly callerUserId: string;
  readonly calleeUserId: string;
  readonly kind: 'Audio' | 'Video';
  readonly acceptedAtUtc: string;
  /** Milisegundos desde ringingAtUtc hasta acceptedAtUtc — métrica ring-to-answer. */
  readonly ringTimeMs: number;
}

export interface CallEndedEvent extends IntegrationEvent {
  readonly eventType: 'communication.call.ended.v1';
  readonly callId: string;
  readonly callerUserId: string;
  readonly calleeUserId: string;
  readonly kind: 'Audio' | 'Video';
  readonly endReason: 'Hangup' | 'Missed' | 'Rejected' | 'Cancelled' | 'IceFailed';
  readonly durationSeconds: number;
  readonly endedAtUtc: string;
  readonly recordingFileId: string | null;
}

export interface CallMissedEvent extends IntegrationEvent {
  readonly eventType: 'communication.call.missed.v1';
  readonly callId: string;
  readonly callerUserId: string;
  readonly calleeUserId: string;
  readonly kind: 'Audio' | 'Video';
  readonly ringingAtUtc: string;
  readonly missedAtUtc: string;
}

export interface CallRecordingReadyEvent extends IntegrationEvent {
  readonly eventType: 'communication.call.recording_ready.v1';
  readonly callId: string;
  readonly recordingFileId: string;
  readonly durationSeconds: number;
  readonly readyAtUtc: string;
  /**
   * Ver docblock de MeetingRecordingReadyEvent.consentSnapshot — mismo
   * criterio, opcional por compatibilidad retro con attach-call-recording.ts.
   */
  readonly consentSnapshot?: readonly RecordingConsentSnapshotEntry[];
}

/** @since Fase Backend 4 — request-call-recording.ts */
export interface CallRecordingConsentRequestedEvent extends IntegrationEvent {
  readonly eventType: 'communication.call.recording_consent_requested.v1';
  readonly callId: string;
  readonly requestedByUserId: string;
  readonly participants: readonly string[];
  readonly requestedAtUtc: string;
}

/** @since Fase Backend 4 — respond-call-recording-consent.ts */
export interface CallRecordingConsentRecordedEvent extends IntegrationEvent {
  readonly eventType: 'communication.call.recording_consent_recorded.v1';
  readonly callId: string;
  readonly userId: string;
  readonly response: RecordingConsentResponse;
  readonly respondedAtUtc: string;
}

/** @since Fase Backend 4 — start-call-recording.ts (auto-invocado desde respond o desde el timeout scheduler) */
export interface CallRecordingStartedEvent extends IntegrationEvent {
  readonly eventType: 'communication.call.recording_started.v1';
  readonly callId: string;
  readonly startedByUserId: string;
  readonly startedAtUtc: string;
  readonly consentSnapshot: readonly RecordingConsentSnapshotEntry[];
}

/** @since Fase Backend 4 — stop-call-recording.ts */
export interface CallRecordingStoppedEvent extends IntegrationEvent {
  readonly eventType: 'communication.call.recording_stopped.v1';
  readonly callId: string;
  readonly stoppedByUserId: string;
  readonly stoppedAtUtc: string;
  readonly elapsedSeconds: number;
}

/**
 * Publicado cuando attach-call-recording.ts recibe el fileId subido y
 * transiciona RecordingSession a Processing — distinto de RecordingReady
 * (que llega despues, cuando el transcript worker confirma).
 * @since Fase Backend 4 — attach-call-recording.ts (transicion Processing)
 */
export interface CallRecordingProcessingStartedEvent extends IntegrationEvent {
  readonly eventType: 'communication.call.recording_processing_started.v1';
  readonly callId: string;
  readonly recordingFileId: string;
  readonly startedAtUtc: string;
}

/**
 * Publicado por el worker de transcripts (proceso separado, Fase 6) al
 * terminar de transcribir `recordingFileId` con whisper.cpp y subir el
 * resultado a CloudStorage. Communication lo consume (transcript-consumers.ts)
 * para adjuntarlo al Call via `attachTranscript`.
 */
export interface CallTranscriptReadyEvent extends IntegrationEvent {
  readonly eventType: 'communication.call.transcript_ready.v1';
  readonly callId: string;
  readonly recordingFileId: string;
  readonly transcriptFileId: string;
  readonly detectedLanguage: string | null;
  /** @since Fase Transcript 5 — derivado por el worker de los timestamps de whisper.cpp. */
  readonly durationSeconds: number;
  /** @since Fase Transcript 5 — conteo de palabras del transcript ya limpio (sin timestamps). */
  readonly wordCount: number;
  readonly readyAtUtc: string;
}

/**
 * @since Fase Backend 7 — upgrade-call-to-video.ts. Marca el cambio de
 * `Call.kind` de Audio a Video; el signaling WebRTC (nueva track) lo resuelve
 * el frontend via renegociacion, este evento solo notifica a otros consumers
 * (Analytics: contar upgrades vs. calls que arrancan como video directo).
 */
export interface CallUpgradedToVideoEvent extends IntegrationEvent {
  readonly eventType: 'communication.call.upgraded_to_video.v1';
  readonly callId: string;
  readonly upgradedByUserId: string;
  readonly upgradedAtUtc: string;
}

/** @since Fase Backend 7 — start-call-screen-share.ts */
export interface CallScreenShareStartedEvent extends IntegrationEvent {
  readonly eventType: 'communication.call.screen_share_started.v1';
  readonly callId: string;
  readonly sharingUserId: string;
  readonly startedAtUtc: string;
}

/** @since Fase Backend 7 — stop-call-screen-share.ts */
export interface CallScreenShareStoppedEvent extends IntegrationEvent {
  readonly eventType: 'communication.call.screen_share_stopped.v1';
  readonly callId: string;
  readonly sharingUserId: string;
  readonly startedAtUtc: string;
  readonly stoppedAtUtc: string;
  readonly durationSeconds: number;
}

/**
 * @since Fase Backend 8 — bug #245. Mismo docblock que MeetingRecordingValidationFailedEvent.
 */
export interface CallRecordingValidationFailedEvent extends IntegrationEvent {
  readonly eventType: 'communication.call.recording_validation_failed.v1';
  readonly callId: string;
  readonly recordingFileId: string;
  readonly failureReason: string;
  readonly detectedAtUtc: string;
}

/** @since Fase Backend 10. Ver docblock de MeetingRecordingFailedEvent — mismo criterio, para calls. */
export interface CallRecordingFailedEvent extends IntegrationEvent {
  readonly eventType: 'communication.call.recording_failed.v1';
  readonly callId: string;
  readonly reason: string;
  readonly failedAtUtc: string;
}
