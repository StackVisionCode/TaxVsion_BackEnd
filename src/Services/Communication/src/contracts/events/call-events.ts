import type { IntegrationEvent } from './integration-event.js';

/**
 * Integration events del ciclo de vida de una llamada. NUNCA llevan payload de
 * SDP/ICE/media — solo IDs, timestamps y counters. El bus queda limpio de PII.
 */
export const CallEventTypes = {
  Started: 'communication.call.started.v1',
  Ended: 'communication.call.ended.v1',
  Missed: 'communication.call.missed.v1',
  RecordingReady: 'communication.call.recording_ready.v1',
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
}
