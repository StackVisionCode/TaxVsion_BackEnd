import type { IntegrationEvent } from './integration-event.js';

/**
 * Integration events del ciclo de vida de una llamada. NUNCA llevan payload de
 * SDP/ICE/media — solo IDs, timestamps y counters. El bus queda limpio de PII.
 */
export const CallEventTypes = {
  Started:            'communication.call.started.v1',
  Accepted:           'communication.call.accepted.v1',
  Ended:              'communication.call.ended.v1',
  Missed:             'communication.call.missed.v1',
  ScreenShareStarted: 'communication.call.screen_share_started.v1',
  ScreenShareStopped: 'communication.call.screen_share_stopped.v1',
  RecordingReady:     'communication.call.recording_ready.v1',
  TranscriptReady:    'communication.call.transcript_ready.v1',
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

export interface CallScreenShareStartedEvent extends IntegrationEvent {
  readonly eventType: 'communication.call.screen_share_started.v1';
  readonly callId: string;
  readonly sharingUserId: string;
  readonly startedAtUtc: string;
}

export interface CallScreenShareStoppedEvent extends IntegrationEvent {
  readonly eventType: 'communication.call.screen_share_stopped.v1';
  readonly callId: string;
  readonly sharingUserId: string;
  readonly startedAtUtc: string;
  readonly stoppedAtUtc: string;
  readonly durationSeconds: number;
}

export interface CallRecordingReadyEvent extends IntegrationEvent {
  readonly eventType: 'communication.call.recording_ready.v1';
  readonly callId: string;
  readonly recordingFileId: string;
  readonly durationSeconds: number;
  readonly readyAtUtc: string;
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
  readonly language: string | null;
  readonly readyAtUtc: string;
}
