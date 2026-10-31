import { z } from 'zod';
import type {
  MessageDeletedDto,
  MessageDto,
  MessageEditedDto,
  ReadReceiptDto,
} from './chat-socket-events.js';

export const JoinMeetingPayloadSchema = z.object({
  clientKey: z.string().min(1).max(128),
  meetingId: z.string().uuid(),
  passcode: z.string().max(120).optional(),
  invitationToken: z.string().length(64).optional(),
  // Preferencia del cliente al unirse — default true (encendido) si se
  // omite, preservando el comportamiento anterior.
  audioDefault: z.boolean().optional(),
  videoDefault: z.boolean().optional(),
});
export type JoinMeetingPayload = z.infer<typeof JoinMeetingPayloadSchema>;

export const LeaveMeetingPayloadSchema = z.object({ meetingId: z.string().uuid() });
export type LeaveMeetingPayload = z.infer<typeof LeaveMeetingPayloadSchema>;

/**
 * Re-unir la room `m:{meetingId}` tras un reconnect TRANSPARENTE del socket (churn del tunnel),
 * sin recargar la página. A diferencia de `join`, NO pide cámara/mic, NO re-negocia media y NO
 * re-pasa por sala de espera: solo re-suscribe al socket a los eventos del meeting (participantes/
 * controles/señalización) para un participante que YA estaba admitido. Devuelve el snapshot para
 * reconciliar la lista de participantes que pudo cambiar durante el corte.
 */
export const RejoinMeetingPayloadSchema = z.object({ meetingId: z.string().uuid() });
export type RejoinMeetingPayload = z.infer<typeof RejoinMeetingPayloadSchema>;

export const AdmitPayloadSchema = z.object({
  meetingId: z.string().uuid(),
  targetUserId: z.string().uuid(),
});
export type AdmitPayload = z.infer<typeof AdmitPayloadSchema>;

export const RemovePayloadSchema = AdmitPayloadSchema;
export type RemovePayload = z.infer<typeof RemovePayloadSchema>;

export const LockPayloadSchema = z.object({
  meetingId: z.string().uuid(),
  locked: z.boolean(),
});
export type LockPayload = z.infer<typeof LockPayloadSchema>;

export const TransferHostPayloadSchema = z.object({
  meetingId: z.string().uuid(),
  newHostUserId: z.string().uuid(),
});
export type TransferHostPayload = z.infer<typeof TransferHostPayloadSchema>;

export const MeetingSignalPayloadSchema = z.object({
  meetingId: z.string().uuid(),
  targetPeerUserId: z.string().uuid(),
  kind: z.enum(['offer', 'answer', 'ice']),
  data: z.record(z.string(), z.unknown()),
});
export type MeetingSignalPayload = z.infer<typeof MeetingSignalPayloadSchema>;

export const MeetingMediaStatusPayloadSchema = z.object({
  meetingId: z.string().uuid(),
  audioEnabled: z.boolean(),
  videoEnabled: z.boolean(),
  screenSharing: z.boolean(),
});
export type MeetingMediaStatusPayload = z.infer<typeof MeetingMediaStatusPayloadSchema>;

export const ConnectionQualityDtoSchema = z.enum(['Unknown', 'Excellent', 'Good', 'Fair', 'Poor', 'Disconnected']);
export type ConnectionQualityDto = z.infer<typeof ConnectionQualityDtoSchema>;

/** F4: metricas opcionales que justifican el nivel; se guardan en el log, no se difunden. */
// Cada cifra puede faltar o venir null (p. ej. sin audio entrante no hay pérdida que medir): nunca se rechaza el reporte.
export const ConnectionQualityMetricsSchema = z.object({
  rttMs: z.number().min(0).nullable().optional(),
  lossPct: z.number().min(0).max(100).nullable().optional(),
  jitterMs: z.number().min(0).nullable().optional(),
  viaRelay: z.boolean().nullable().optional(),
  inKbps: z.number().min(0).nullable().optional(),
  outKbps: z.number().min(0).nullable().optional(),
  fps: z.number().min(0).nullable().optional(),
  // Capa activa: índice numérico (0..2) o etiqueta del navegador ('r2', '720p').
  layer: z.union([z.number().int().min(0), z.string().max(16)]).nullable().optional(),
});

export const MeetingConnectionQualityPayloadSchema = z.object({
  meetingId: z.string().uuid(),
  quality: z.enum(['Excellent', 'Good', 'Fair', 'Poor', 'Disconnected']),
  metrics: ConnectionQualityMetricsSchema.optional(),
});
export type MeetingConnectionQualityPayload = z.infer<typeof MeetingConnectionQualityPayloadSchema>;

export interface MeetingParticipantQualityChangedDto {
  meetingId: string;
  userId: string;
  quality: ConnectionQualityDto;
}

export const MeetingRaiseHandPayloadSchema = z.object({
  meetingId: z.string().uuid(),
  raised: z.boolean(),
});
export type MeetingRaiseHandPayload = z.infer<typeof MeetingRaiseHandPayloadSchema>;

/** F5 (D-49): acciones de host sobre manos. */
export const LowerHandPayloadSchema = AdmitPayloadSchema;
export type LowerHandPayload = z.infer<typeof LowerHandPayloadSchema>;
export const LowerAllHandsPayloadSchema = z.object({ meetingId: z.string().uuid() });
export type LowerAllHandsPayload = z.infer<typeof LowerAllHandsPayloadSchema>;

/** F5 (D-50): reaccion efimera; el servidor la valida contra el catalogo efectivo. */
export const MeetingReactionPayloadSchema = z.object({
  meetingId: z.string().uuid(),
  emoji: z.string().min(1).max(16),
});
export type MeetingReactionPayload = z.infer<typeof MeetingReactionPayloadSchema>;

/** F5 (T-69): cambio del estado de habla propio (RMS local). Solo alimenta el MeetingEventLog. */
export const MeetingSpeakingPayloadSchema = z.object({
  meetingId: z.string().uuid(),
  speaking: z.boolean(),
});
export type MeetingSpeakingPayload = z.infer<typeof MeetingSpeakingPayloadSchema>;

export const AttachMeetingRecordingPayloadSchema = z.object({
  clientKey: z.string().min(1).max(128),
  meetingId: z.string().uuid(),
  // fileId ya subido a CloudStorage por el cliente (mismo patron que en calls).
  fileId: z.string().uuid(),
});
export type AttachMeetingRecordingPayload = z.infer<typeof AttachMeetingRecordingPayloadSchema>;

// ---------- Recording consent (Fase Backend 3 — ver domain/meetings/recording-session.ts) ----------
// clientKey en start/stop porque disparan integration events y deben ser
// idempotentes ante reintentos del cliente (mismo criterio que AttachRecording).

export const RequestMeetingRecordingPayloadSchema = z.object({
  clientKey: z.string().min(1).max(128),
  meetingId: z.string().uuid(),
});
export type RequestMeetingRecordingPayload = z.infer<typeof RequestMeetingRecordingPayloadSchema>;

export const StopMeetingRecordingPayloadSchema = z.object({
  clientKey: z.string().min(1).max(128),
  meetingId: z.string().uuid(),
});
export type StopMeetingRecordingPayload = z.infer<typeof StopMeetingRecordingPayloadSchema>;

export const RespondMeetingRecordingConsentPayloadSchema = z.object({
  meetingId: z.string().uuid(),
  response: z.enum(['Accepted', 'Rejected']),
});
export type RespondMeetingRecordingConsentPayload = z.infer<
  typeof RespondMeetingRecordingConsentPayloadSchema
>;

// ---------- Host actions adicionales (Fase Backend 6) ----------

export const DenyParticipantPayloadSchema = z.object({
  meetingId: z.string().uuid(),
  targetUserId: z.string().uuid(),
});
export type DenyParticipantPayload = z.infer<typeof DenyParticipantPayloadSchema>;

export const CancelMeetingPayloadSchema = z.object({
  meetingId: z.string().uuid(),
  reason: z.string().max(500).optional(),
});
export type CancelMeetingPayload = z.infer<typeof CancelMeetingPayloadSchema>;

export const RescheduleMeetingPayloadSchema = z.object({
  meetingId: z.string().uuid(),
  newScheduledForUtc: z.string().datetime(),
});
export type RescheduleMeetingPayload = z.infer<typeof RescheduleMeetingPayloadSchema>;

export const PromoteCohostPayloadSchema = z.object({
  meetingId: z.string().uuid(),
  targetUserId: z.string().uuid(),
});
export type PromoteCohostPayload = z.infer<typeof PromoteCohostPayloadSchema>;

export const DemoteCohostPayloadSchema = PromoteCohostPayloadSchema;
export type DemoteCohostPayload = z.infer<typeof DemoteCohostPayloadSchema>;

export interface MeetingTranscriptReadyDto {
  meetingId: string;
  transcriptFileId: string;
  detectedLanguage: string | null;
  /** Fase Transcript 5 — duracion del audio en segundos, derivada por el worker de los timestamps de whisper.cpp. */
  durationSeconds: number;
  /** Fase Transcript 5 — conteo de palabras del transcript, para previsualizacion sin descargar el archivo completo. */
  wordCount: number;
  readyAtUtc: string;
  /** F7 (T-28): transcript v2 con hablantes (JSON) y su resumen. */
  transcriptJsonFileId?: string | null;
  speakerCount?: number;
  segmentCount?: number;
  language?: string | null;
}

// ---------- SFU (mediasoup) — meetings con strategy 'Sfu', >4 participantes ----------
// Los objetos WebRTC (dtlsParameters/rtpParameters/rtpCapabilities) son
// opacos al server, mismo criterio que `data` en MeetingSignalPayloadSchema
// — mediasoup los valida internamente al usarlos (ver sfu-signaling.ts).

export const SfuRouterCapabilitiesPayloadSchema = z.object({ meetingId: z.string().uuid() });
export type SfuRouterCapabilitiesPayload = z.infer<typeof SfuRouterCapabilitiesPayloadSchema>;

export const SfuCreateTransportPayloadSchema = z.object({
  meetingId: z.string().uuid(),
  direction: z.enum(['send', 'recv']),
});
export type SfuCreateTransportPayload = z.infer<typeof SfuCreateTransportPayloadSchema>;

export const SfuConnectTransportPayloadSchema = z.object({
  meetingId: z.string().uuid(),
  transportId: z.string().min(1),
  dtlsParameters: z.record(z.string(), z.unknown()),
});
export type SfuConnectTransportPayload = z.infer<typeof SfuConnectTransportPayloadSchema>;

export const SfuProducePayloadSchema = z.object({
  meetingId: z.string().uuid(),
  transportId: z.string().min(1),
  kind: z.enum(['audio', 'video']),
  rtpParameters: z.record(z.string(), z.unknown()),
  // Distingue la cámara del screen-share cuando un participante produce DOS videos a la vez (Zoom-style).
  // Opcional/retrocompatible: un cliente viejo que no lo manda queda como 'camera'.
  source: z.enum(['camera', 'screen']).optional().default('camera'),
});
export type SfuProducePayload = z.infer<typeof SfuProducePayloadSchema>;

export const SfuConsumePayloadSchema = z.object({
  meetingId: z.string().uuid(),
  transportId: z.string().min(1),
  producerId: z.string().min(1),
  rtpCapabilities: z.record(z.string(), z.unknown()),
});
export type SfuConsumePayload = z.infer<typeof SfuConsumePayloadSchema>;

export const SfuResumeConsumerPayloadSchema = z.object({
  meetingId: z.string().uuid(),
  consumerId: z.string().min(1),
});
export type SfuResumeConsumerPayload = z.infer<typeof SfuResumeConsumerPayloadSchema>;

// Capa preferida por consumidor (simulcast): el cliente pide capa alta para el tile destacado
// (spotlight) y baja para los thumbnails. spatialLayer 0/1/2 = ¼/½/full (encodings r0/r1/r2).
export const SfuSetPreferredLayersPayloadSchema = z.object({
  meetingId: z.string().uuid(),
  consumerId: z.string().min(1),
  spatialLayer: z.number().int().min(0).max(2),
  temporalLayer: z.number().int().min(0).max(2).optional(),
});
export type SfuSetPreferredLayersPayload = z.infer<typeof SfuSetPreferredLayersPayloadSchema>;

export const SfuListRemoteProducersPayloadSchema = z.object({ meetingId: z.string().uuid() });
export type SfuListRemoteProducersPayload = z.infer<typeof SfuListRemoteProducersPayloadSchema>;

// F1 — ICE restart de un transport del SFU (tras `failed` o `disconnected` sostenido en el cliente).
// El ack devuelve los `iceParameters` nuevos que el cliente aplica con `transport.restartIce()`.
export const SfuRestartIcePayloadSchema = z.object({
  meetingId: z.string().uuid(),
  transportId: z.string().min(1),
});
export type SfuRestartIcePayload = z.infer<typeof SfuRestartIcePayloadSchema>;

// ---------- Chat dentro del meeting (Fase 8) ----------
// Reusa integramente el motor de chat (Conversation kind 'Meeting',
// sendMessage/editMessage/deleteMessage/markMessagesRead) — estos payloads
// solo llevan `meetingId` en vez de `conversationId` porque autorizar por
// "estas Joined en este meeting" (no por `communication.chat.reply`) es la
// semantica correcta aca; el handler resuelve el conversationId server-side.

export const MeetingChatSendPayloadSchema = z.object({
  clientKey: z.string().min(1).max(128),
  meetingId: z.string().uuid(),
  body: z.string().min(1).max(4000).optional(),
  attachmentFileId: z.string().uuid().optional(),
  replyToMessageId: z.string().uuid().optional(),
});
export type MeetingChatSendPayload = z.infer<typeof MeetingChatSendPayloadSchema>;

export const MeetingChatEditPayloadSchema = z.object({
  clientKey: z.string().min(1).max(128),
  meetingId: z.string().uuid(),
  messageId: z.string().uuid(),
  body: z.string().min(1).max(4000),
});
export type MeetingChatEditPayload = z.infer<typeof MeetingChatEditPayloadSchema>;

export const MeetingChatDeletePayloadSchema = z.object({
  clientKey: z.string().min(1).max(128),
  meetingId: z.string().uuid(),
  messageId: z.string().uuid(),
});
export type MeetingChatDeletePayload = z.infer<typeof MeetingChatDeletePayloadSchema>;

export const MeetingChatMarkReadPayloadSchema = z.object({
  clientKey: z.string().min(1).max(128),
  meetingId: z.string().uuid(),
  lastReadMessageId: z.string().uuid(),
});
export type MeetingChatMarkReadPayload = z.infer<typeof MeetingChatMarkReadPayloadSchema>;

// ---------- DTOs server -> client ----------

export interface MeetingParticipantDto {
  userId: string;
  displayName: string;
  /** F3 — "TenantEmployee" | "TenantAdmin" | "CustomerPortal" | "Guest"...: el front decide badges y elegibilidad. */
  actorType?: string;
  /**
   * F5 avatares — foto resuelta al armar el snapshot (join/rejoin/admit) y el participante que entra.
   * Ausente en los `participant.changed` de media/mano/salida: conservar la que ya se tenga. null = sin foto.
   */
  avatarFileId?: string | null;
  role: 'Host' | 'Cohost' | 'Attendee';
  status: 'Waiting' | 'Joined' | 'Left' | 'Removed';
  joinOrder: number;
  audioEnabled: boolean;
  videoEnabled: boolean;
  screenSharing: boolean;
  handRaised: boolean;
  /** F5 (D-49): ISO de cuando levanto la mano; null = baja. Ordena la cola de manos. */
  handRaisedAtUtc: string | null;
  /** F4 (D-39): ultimo nivel reportado por ese participante; 'Unknown' hasta el primer muestreo. */
  connectionQuality?: ConnectionQualityDto;
}

export type MeetingStatusDto = 'Scheduled' | 'Live' | 'Ended' | 'Cancelled' | 'Expired';
export type MeetingEndReasonDto = 'Host' | 'MaxDurationReached' | 'NoParticipants' | 'Offboarding' | 'Backfill';

export interface MeetingSnapshotDto {
  meetingId: string;
  status: MeetingStatusDto;
  strategy: 'Mesh' | 'Sfu';
  hostUserId: string;
  isLocked: boolean;
  participants: readonly MeetingParticipantDto[];
  yourRole: 'Host' | 'Cohost' | 'Attendee';
  sequence: number;
  // null mientras el join esta en la sala de espera (requiresAdmission=true)
  // — el chat del meeting recien se crea/asocia cuando efectivamente se entra.
  conversationId: string | null;
  // F3 (T-40) — el reloj del cliente se calcula desde `endsAtUtc` del servidor, nunca desde el suyo.
  scheduledDurationMinutes: number | null;
  maxDurationMinutes: number | null;
  endsAtUtc: string | null;
  endReason: MeetingEndReasonDto | null;
  continuationOfMeetingId: string | null;
}

export interface MeetingParticipantChangedDto {
  meetingId: string;
  participant: MeetingParticipantDto;
  sequence: number;
}

export interface MeetingSignalDto {
  meetingId: string;
  fromPeerUserId: string;
  kind: 'offer' | 'answer' | 'ice';
  data: Record<string, unknown>;
}

export interface MeetingStateDto {
  meetingId: string;
  status: MeetingStatusDto;
  isLocked: boolean;
  hostUserId: string;
  sequence: number;
  /** F3 — presente cuando status es Ended. */
  endReason?: MeetingEndReasonDto | null;
}

/** F3 — aviso de tiempo restante (room del meeting), una vez por umbral. */
export interface MeetingDurationWarningDto {
  meetingId: string;
  remainingSeconds: number;
  endsAtUtc: string;
}

/** F3 — cambió el host (transfer, sucesión al vencer la gracia, offboarding). Va tras los `participant.changed`. */
export interface MeetingHostChangedDto {
  meetingId: string;
  previousHostUserId: string;
  newHostUserId: string;
  reason: 'Transfer' | 'Succession' | 'Offboarding';
}

/** F3 — dirigido al socket que el sistema saca de la sala (gracia, revocación, token vencido, retiro). */
export interface MeetingYouRemovedDto {
  meetingId: string;
  reason: 'Grace' | 'Revoked' | 'Expired' | 'Offboarding' | 'MeetingEnded' | 'RemovedByHost';
}

/** F3 — `meeting.ended` ahora lleva el motivo (lista y sala). */
export interface MeetingEndedDto {
  meetingId: string;
  reason: MeetingEndReasonDto | 'Expired';
}

/** F5 (D-49): ack de `meeting.raise_hand`. */
export interface MeetingRaiseHandAckDto {
  handRaised: boolean;
  handRaisedAtUtc: string | null;
}

/** F5 (D-49): a la room de usuario del afectado cuando el host le baja la mano (o todas). */
export interface MeetingHandLoweredByHostDto {
  meetingId: string;
  byUserId: string;
  all: boolean;
}

/** F5 (D-50): reaccion efimera difundida con emision volatil; nunca se persiste por unidad. */
export interface MeetingReactionReceivedDto {
  meetingId: string;
  userId: string;
  displayName: string;
  emoji: string;
  at: string;
}

export interface SfuNewProducerDto {
  meetingId: string;
  userId: string;
  producerId: string;
  kind: 'audio' | 'video';
  /** 'camera' (default) o 'screen' — para que el receptor pinte cámara vs pantalla en tiles separados. */
  source: 'camera' | 'screen';
}

export interface SfuProducerClosedDto {
  meetingId: string;
  userId: string;
  producerId: string;
}

/**
 * Estado de la RecordingSession del meeting (Fase Backend 2/3). Idle = nunca
 * se pidio grabar en esta sesion de meeting; Requesting = esperando
 * respuestas de consentimiento; Processing = el archivo ya se adjunto y el
 * transcript worker lo esta procesando; Ready/Failed son terminales.
 */
export type MeetingRecordingState =
  | 'Idle'
  | 'Requesting'
  | 'Recording'
  | 'Stopping'
  | 'Composing'
  | 'Processing'
  | 'Ready'
  | 'Failed';

export interface MeetingRecordingConsentRequestedDto {
  meetingId: string;
  requestedByUserId: string;
  requestedAtUtc: string;
}

export interface MeetingRecordingStateChangedDto {
  meetingId: string;
  state: MeetingRecordingState;
  updatedAtUtc: string;
  /** F7: quien captura (`Server` = recorder; `Client` = mezcla en el navegador, fallback C-03). */
  captureMode?: 'Server' | 'Client';
  /** F7: inicio real y limite efectivo; el front calcula la cuenta regresiva con el reloj del servidor. */
  startedAtUtc?: string | null;
  maxDurationSeconds?: number | null;
  /** F7: en Stopping = User|LimitReached|MeetingEnded|CallEnded; en Failed = codigo del fallo. */
  reason?: string | null;
}

/** F7 (T-35): la grabacion se corta pronto por el limite (una sola vez por sesion). */
export interface MeetingRecordingLimitWarningDto {
  meetingId: string;
  remainingSeconds: number;
  stopsAtUtc: string;
}

/**
 * @since Fase Frontend 3 — el "nudge" de RecordingStateChanged no lleva quien
 * respondio ni con que respuesta, solo que el estado agregado (todavia
 * Requesting) puede haber cambiado internamente. Sin este evento, ningun
 * participante puede armar una UI de "quien acepto/rechazo" en tiempo real —
 * `MeetingRecordingConsentRecordedEvent` (integration event interno) ya
 * llevaba esta info, solo faltaba re-emitirla por socket a la room.
 */
export interface MeetingRecordingConsentRecordedDto {
  meetingId: string;
  userId: string;
  response: 'Accepted' | 'Rejected';
  respondedAtUtc: string;
}

// ---------- Host actions Fase Backend 6 (S→C) ----------

export interface MeetingCancelledDto {
  meetingId: string;
  cancelledByUserId: string;
  reason: string | null;
  cancelledAtUtc: string;
}

export interface MeetingRescheduledDto {
  meetingId: string;
  rescheduledByUserId: string;
  previousScheduledForUtc: string | null;
  newScheduledForUtc: string | null;
  rescheduledAtUtc: string;
}

export interface MeetingParticipantDeniedDto {
  meetingId: string;
  participantUserId: string;
  deniedByUserId: string;
  deniedAtUtc: string;
}

/**
 * Aviso dirigido al room del usuario (`t:{tenant}:u:{userId}`) para que su lista de meetings
 * (CRM/Portal) se refresque en tiempo real. `Invited` se emite al invitado al crear su
 * invitacion; `Started` a los invitados/participantes cuando el host arranca el meeting. El
 * payload es minimo (solo `meetingId`) — la lista es autoritativa via refetch, no se parchea
 * desde el evento.
 */
export interface MeetingListChangedDto {
  meetingId: string;
}

/** F6 (T-62): `meeting.invited` ahora dice quien invita y si la reunion ya esta en curso (toast "Join"). */
export interface MeetingInvitedDto extends MeetingListChangedDto {
  title: string;
  status: 'Scheduled' | 'Live';
  invitedByUserId: string;
  invitedByDisplayName: string;
}

/** F6 (T-64): recordatorio T-N a la room de usuario de invitados y participantes. */
export interface MeetingReminderDto {
  meetingId: string;
  title: string;
  scheduledForUtc: string;
  minutesBefore: number;
}

export const MeetingSocketEvents = {
  // c -> s
  Join: 'meeting.join',
  Rejoin: 'meeting.rejoin',
  Leave: 'meeting.leave',
  Admit: 'meeting.host.admit',
  Remove: 'meeting.host.remove',
  Lock: 'meeting.host.lock',
  MuteAll: 'meeting.host.mute_all',
  TransferHost: 'meeting.host.transfer',
  Signal: 'meeting.signal',
  MediaStatus: 'meeting.media_status',
  RaiseHand: 'meeting.raise_hand',
  // F4 (T-48): el cliente reporta su nivel (solo al cambiar; rate limit por socket).
  ConnectionQuality: 'meeting.connection_quality',
  LowerHand: 'meeting.host.lower_hand',
  LowerAllHands: 'meeting.host.lower_all_hands',
  Reaction: 'meeting.reaction',
  Speaking: 'meeting.speaking',
  AttachRecording: 'meeting.recording.attach',
  RequestRecording: 'meeting.recording.start_request',
  StopRecording: 'meeting.recording.stop',
  RespondRecordingConsent: 'meeting.consent.respond',
  DenyParticipant: 'meeting.host.deny_participant',
  CancelMeeting: 'meeting.host.cancel',
  RescheduleMeeting: 'meeting.host.reschedule',
  PromoteCohost: 'meeting.host.promote_cohost',
  DemoteCohost: 'meeting.host.demote_cohost',
  SfuGetRouterCapabilities: 'meeting.sfu.get_router_capabilities',
  SfuCreateTransport: 'meeting.sfu.create_transport',
  SfuConnectTransport: 'meeting.sfu.connect_transport',
  SfuProduce: 'meeting.sfu.produce',
  SfuConsume: 'meeting.sfu.consume',
  SfuResumeConsumer: 'meeting.sfu.resume_consumer',
  SfuSetPreferredLayers: 'meeting.sfu.set_preferred_layers',
  SfuListRemoteProducers: 'meeting.sfu.list_remote_producers',
  SfuRestartIce: 'meeting.sfu.restart_ice',
  ChatSend: 'meeting.chat.send',
  ChatEdit: 'meeting.chat.edit',
  ChatDelete: 'meeting.chat.delete',
  ChatMarkRead: 'meeting.chat.mark_read',
  // s -> c
  Snapshot: 'meeting.snapshot',
  ParticipantChanged: 'meeting.participant.changed',
  StateChanged: 'meeting.state.changed',
  SignalFrom: 'meeting.signal.from',
  HandLoweredByHost: 'meeting.hand.lowered_by_host',
  ReactionReceived: 'meeting.reaction.received',
  MutedByHost: 'meeting.you.muted',
  SfuNewProducer: 'meeting.sfu.new_producer',
  SfuProducerClosed: 'meeting.sfu.producer_closed',
  // F3 — ciclo de vida y host.
  DurationWarning: 'meeting.duration.warning',
  // F4 (D-39): nivel de otro participante, para su tile.
  ParticipantQualityChanged: 'meeting.participant.quality_changed',
  HostChanged: 'meeting.host.changed',
  YouRemoved: 'meeting.you.removed',
  TranscriptReady: 'meeting.transcript_ready',
  RecordingConsentRequested: 'meeting.recording.consent_requested',
  RecordingConsentRecorded: 'meeting.recording.consent_recorded',
  RecordingStateChanged: 'meeting.recording.state_changed',
  RecordingLimitWarning: 'meeting.recording.limit_warning',
  ParticipantDenied: 'meeting.participant.denied',
  Cancelled: 'meeting.cancelled',
  Rescheduled: 'meeting.rescheduled',
  // Avisos para la LISTA de meetings (no la sala): refrescan el UX de meetings del invitado.
  Invited: 'meeting.invited',
  Reminder: 'meeting.reminder',
  Started: 'meeting.started',
  // Terminó (por End explícito o cascada host-sin-cohost): la lista mueve el meeting de upcoming a past.
  Ended: 'meeting.ended',
  ChatMessageNew: 'meeting.chat.message.new',
  ChatMessageEdited: 'meeting.chat.message.edited',
  ChatMessageDeleted: 'meeting.chat.message.deleted',
  ChatMessageRead: 'meeting.chat.message.read',
} as const;

// Re-exportados para que los handlers de meeting no necesiten importar
// directamente de chat-socket-events.ts — mismos DTOs, distinto namespace de eventos.
export type { MessageDto, MessageEditedDto, MessageDeletedDto, ReadReceiptDto };
