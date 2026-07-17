import { z } from 'zod';

/**
 * Contratos socket para llamadas 1:1. Todos los payloads client->server pasan
 * por Zod. Server->client van con SocketEnvelope<T>.
 *
 * Signaling: cliente NUNCA broadcast — siempre carga `targetPeerId` explicito
 * y el server valida que el peer sea el otro participante. Cierre del bug
 * legacy de emisiones cross-tenant.
 */

// ---------- Payloads cliente -> server (Zod) ----------

export const InitiateCallPayloadSchema = z.object({
  clientKey: z.string().min(1).max(128),
  calleeUserId: z.string().uuid(),
  kind: z.enum(['Audio', 'Video']),
  conversationId: z.string().uuid().optional(),
  recordingRequested: z.boolean().optional(),
});
export type InitiateCallPayload = z.infer<typeof InitiateCallPayloadSchema>;

export const CallActionPayloadSchema = z.object({
  clientKey: z.string().min(1).max(128),
  callId: z.string().uuid(),
});
export type CallActionPayload = z.infer<typeof CallActionPayloadSchema>;

export const CallSignalPayloadSchema = z.object({
  callId: z.string().uuid(),
  targetPeerUserId: z.string().uuid(),
  kind: z.enum(['offer', 'answer', 'ice']),
  // El body SDP/ICE es opaco al server (nunca se parsea).
  data: z.record(z.string(), z.unknown()),
});
export type CallSignalPayload = z.infer<typeof CallSignalPayloadSchema>;

export const MediaStatusPayloadSchema = z.object({
  callId: z.string().uuid(),
  audioEnabled: z.boolean(),
  videoEnabled: z.boolean(),
  screenSharing: z.boolean(),
});
export type MediaStatusPayload = z.infer<typeof MediaStatusPayloadSchema>;

export const ConnectionQualityPayloadSchema = z.object({
  callId: z.string().uuid(),
  quality: z.enum(['Excellent', 'Good', 'Fair', 'Poor', 'Disconnected']),
});
export type ConnectionQualityPayload = z.infer<typeof ConnectionQualityPayloadSchema>;

export const AttachCallRecordingPayloadSchema = z.object({
  clientKey: z.string().min(1).max(128),
  callId: z.string().uuid(),
  // fileId ya subido a CloudStorage por el cliente (MediaRecorder -> upload
  // directo, fuera de este servicio — mismo patron que attachmentFileId de chat).
  fileId: z.string().uuid(),
});
export type AttachCallRecordingPayload = z.infer<typeof AttachCallRecordingPayloadSchema>;

// ---------- Recording consent (Fase Backend 4) ----------
// Policy fija AllAcceptedRequired (solo 2 partes) — ver pending/call-pending-events.ts.
// clientKey en start/stop por el mismo motivo que en meetings: idempotencia
// ante reintentos del cliente al disparar integration events.

export const RequestCallRecordingPayloadSchema = z.object({
  clientKey: z.string().min(1).max(128),
  callId: z.string().uuid(),
});
export type RequestCallRecordingPayload = z.infer<typeof RequestCallRecordingPayloadSchema>;

export const StopCallRecordingPayloadSchema = z.object({
  clientKey: z.string().min(1).max(128),
  callId: z.string().uuid(),
});
export type StopCallRecordingPayload = z.infer<typeof StopCallRecordingPayloadSchema>;

export const RespondCallRecordingConsentPayloadSchema = z.object({
  callId: z.string().uuid(),
  response: z.enum(['Accepted', 'Rejected']),
});
export type RespondCallRecordingConsentPayload = z.infer<
  typeof RespondCallRecordingConsentPayloadSchema
>;

export interface CallTranscriptReadyDto {
  callId: string;
  transcriptFileId: string;
  detectedLanguage: string | null;
  /** Fase Transcript 5 — duracion del audio en segundos, derivada por el worker de los timestamps de whisper.cpp. */
  durationSeconds: number;
  /** Fase Transcript 5 — conteo de palabras del transcript, para previsualizacion sin descargar el archivo completo. */
  wordCount: number;
  readyAtUtc: string;
}

// ---------- DTOs server -> client ----------

export interface IncomingCallDto {
  callId: string;
  callerUserId: string;
  callerDisplayName: string;
  calleeUserId: string;
  kind: 'Audio' | 'Video';
  conversationId: string | null;
  ringingAtUtc: string;
}

export interface CallStateDto {
  callId: string;
  status: 'Ringing' | 'Accepted' | 'Active' | 'Ended' | 'Rejected' | 'Cancelled' | 'MissedCall' | 'Failed';
  endReason: 'Hangup' | 'Missed' | 'Rejected' | 'Cancelled' | 'IceFailed' | null;
  durationSeconds: number | null;
  updatedAtUtc: string;
}

export interface CallPeerDto {
  callId: string;
  peerUserId: string;
  displayName: string;
  role: 'Caller' | 'Callee';
  joinOrder: number;
  isPolite: boolean;
}

export interface CallSignalDto {
  callId: string;
  fromPeerUserId: string;
  kind: 'offer' | 'answer' | 'ice';
  data: Record<string, unknown>;
}

export interface CallMediaStatusDto {
  callId: string;
  peerUserId: string;
  audioEnabled: boolean;
  videoEnabled: boolean;
  screenSharing: boolean;
}

/** Ver docblock de MeetingRecordingState — mismo modelo, para calls. */
export type CallRecordingState =
  | 'Idle'
  | 'Requesting'
  | 'Recording'
  | 'Stopping'
  | 'Processing'
  | 'Ready'
  | 'Failed';

export interface CallRecordingConsentRequestedDto {
  callId: string;
  requestedByUserId: string;
  requestedAtUtc: string;
}

export interface CallRecordingStateChangedDto {
  callId: string;
  state: CallRecordingState;
  updatedAtUtc: string;
}

/** @since Fase Frontend 4 — ver docblock de MeetingRecordingConsentRecordedDto, mismo criterio para calls. */
export interface CallRecordingConsentRecordedDto {
  callId: string;
  userId: string;
  response: 'Accepted' | 'Rejected';
  respondedAtUtc: string;
}

// ---------- Fase Backend 7 — upgrade + screen share dedicados ----------

export const UpgradeCallToVideoPayloadSchema = z.object({
  clientKey: z.string().min(1).max(128),
  callId: z.string().uuid(),
});
export type UpgradeCallToVideoPayload = z.infer<typeof UpgradeCallToVideoPayloadSchema>;

export const StartCallScreenSharePayloadSchema = z.object({
  clientKey: z.string().min(1).max(128),
  callId: z.string().uuid(),
});
export type StartCallScreenSharePayload = z.infer<typeof StartCallScreenSharePayloadSchema>;

export const StopCallScreenSharePayloadSchema = z.object({
  clientKey: z.string().min(1).max(128),
  callId: z.string().uuid(),
});
export type StopCallScreenSharePayload = z.infer<typeof StopCallScreenSharePayloadSchema>;

export interface CallUpgradedToVideoDto {
  callId: string;
  upgradedByUserId: string;
  upgradedAtUtc: string;
}

export interface CallScreenShareStartedDto {
  callId: string;
  sharingUserId: string;
  startedAtUtc: string;
}

export interface CallScreenShareStoppedDto {
  callId: string;
  sharingUserId: string;
  startedAtUtc: string;
  stoppedAtUtc: string;
  durationSeconds: number;
}

// ---------- Nombres de eventos ----------

export const CallSocketEvents = {
  // c -> s
  Initiate: 'call.initiate',
  Accept: 'call.accept',
  Reject: 'call.reject',
  Cancel: 'call.cancel',
  End: 'call.end',
  Signal: 'call.signal',
  MediaStatus: 'call.media_status',
  ConnectionQuality: 'call.connection_quality',
  AttachRecording: 'call.recording.attach',
  RequestRecording: 'call.recording.start_request',
  StopRecording: 'call.recording.stop',
  RespondRecordingConsent: 'call.consent.respond',
  UpgradeToVideo: 'call.upgrade_to_video',
  ScreenShareStart: 'call.screen_share.start',
  ScreenShareStop: 'call.screen_share.stop',
  // s -> c
  Incoming: 'call.incoming',
  StateChanged: 'call.state_changed',
  PeerJoined: 'call.peer_joined',
  SignalFrom: 'call.signal_from',
  MediaStatusChanged: 'call.media_status_changed',
  TranscriptReady: 'call.transcript_ready',
  RecordingConsentRequested: 'call.recording.consent_requested',
  RecordingConsentRecorded: 'call.recording.consent_recorded',
  RecordingStateChanged: 'call.recording.state_changed',
  UpgradedToVideo: 'call.upgraded_to_video',
  ScreenShareStarted: 'call.screen_share.started',
  ScreenShareStopped: 'call.screen_share.stopped',
} as const;
