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
  // s -> c
  Incoming: 'call.incoming',
  StateChanged: 'call.state_changed',
  PeerJoined: 'call.peer_joined',
  SignalFrom: 'call.signal_from',
  MediaStatusChanged: 'call.media_status_changed',
} as const;
