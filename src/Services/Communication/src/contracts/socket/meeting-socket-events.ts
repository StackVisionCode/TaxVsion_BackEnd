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
});
export type JoinMeetingPayload = z.infer<typeof JoinMeetingPayloadSchema>;

export const LeaveMeetingPayloadSchema = z.object({ meetingId: z.string().uuid() });
export type LeaveMeetingPayload = z.infer<typeof LeaveMeetingPayloadSchema>;

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

export const MeetingRaiseHandPayloadSchema = z.object({
  meetingId: z.string().uuid(),
  raised: z.boolean(),
});
export type MeetingRaiseHandPayload = z.infer<typeof MeetingRaiseHandPayloadSchema>;

export const DominantSpeakerPayloadSchema = z.object({
  meetingId: z.string().uuid(),
  audioLevel: z.number().min(0).max(1),
});
export type DominantSpeakerPayload = z.infer<typeof DominantSpeakerPayloadSchema>;

export const AttachMeetingRecordingPayloadSchema = z.object({
  clientKey: z.string().min(1).max(128),
  meetingId: z.string().uuid(),
  // fileId ya subido a CloudStorage por el cliente (mismo patron que en calls).
  fileId: z.string().uuid(),
});
export type AttachMeetingRecordingPayload = z.infer<typeof AttachMeetingRecordingPayloadSchema>;

export interface MeetingTranscriptReadyDto {
  meetingId: string;
  transcriptFileId: string;
  language: string | null;
  readyAtUtc: string;
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

export const SfuListRemoteProducersPayloadSchema = z.object({ meetingId: z.string().uuid() });
export type SfuListRemoteProducersPayload = z.infer<typeof SfuListRemoteProducersPayloadSchema>;

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
  role: 'Host' | 'Cohost' | 'Attendee';
  status: 'Waiting' | 'Joined' | 'Left' | 'Removed';
  joinOrder: number;
  audioEnabled: boolean;
  videoEnabled: boolean;
  screenSharing: boolean;
  handRaised: boolean;
}

export interface MeetingSnapshotDto {
  meetingId: string;
  status: 'Scheduled' | 'Live' | 'Ended' | 'Cancelled';
  strategy: 'Mesh' | 'Sfu';
  hostUserId: string;
  isLocked: boolean;
  participants: readonly MeetingParticipantDto[];
  yourRole: 'Host' | 'Cohost' | 'Attendee';
  sequence: number;
  // null mientras el join esta en la sala de espera (requiresAdmission=true)
  // — el chat del meeting recien se crea/asocia cuando efectivamente se entra.
  conversationId: string | null;
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
  status: 'Scheduled' | 'Live' | 'Ended' | 'Cancelled';
  isLocked: boolean;
  hostUserId: string;
  sequence: number;
}

export interface MeetingDominantSpeakerDto {
  meetingId: string;
  peerUserId: string;
  audioLevel: number;
}

export interface SfuNewProducerDto {
  meetingId: string;
  userId: string;
  producerId: string;
  kind: 'audio' | 'video';
}

export interface SfuProducerClosedDto {
  meetingId: string;
  userId: string;
  producerId: string;
}

export const MeetingSocketEvents = {
  // c -> s
  Join: 'meeting.join',
  Leave: 'meeting.leave',
  Admit: 'meeting.host.admit',
  Remove: 'meeting.host.remove',
  Lock: 'meeting.host.lock',
  MuteAll: 'meeting.host.mute_all',
  TransferHost: 'meeting.host.transfer',
  Signal: 'meeting.signal',
  MediaStatus: 'meeting.media_status',
  RaiseHand: 'meeting.raise_hand',
  DominantSpeaker: 'meeting.dominant_speaker',
  AttachRecording: 'meeting.recording.attach',
  SfuGetRouterCapabilities: 'meeting.sfu.get_router_capabilities',
  SfuCreateTransport: 'meeting.sfu.create_transport',
  SfuConnectTransport: 'meeting.sfu.connect_transport',
  SfuProduce: 'meeting.sfu.produce',
  SfuConsume: 'meeting.sfu.consume',
  SfuResumeConsumer: 'meeting.sfu.resume_consumer',
  SfuListRemoteProducers: 'meeting.sfu.list_remote_producers',
  ChatSend: 'meeting.chat.send',
  ChatEdit: 'meeting.chat.edit',
  ChatDelete: 'meeting.chat.delete',
  ChatMarkRead: 'meeting.chat.mark_read',
  // s -> c
  Snapshot: 'meeting.snapshot',
  ParticipantChanged: 'meeting.participant.changed',
  StateChanged: 'meeting.state.changed',
  SignalFrom: 'meeting.signal.from',
  DominantSpeakerChanged: 'meeting.dominant_speaker.changed',
  MutedByHost: 'meeting.you.muted',
  SfuNewProducer: 'meeting.sfu.new_producer',
  SfuProducerClosed: 'meeting.sfu.producer_closed',
  TranscriptReady: 'meeting.transcript_ready',
  ChatMessageNew: 'meeting.chat.message.new',
  ChatMessageEdited: 'meeting.chat.message.edited',
  ChatMessageDeleted: 'meeting.chat.message.deleted',
  ChatMessageRead: 'meeting.chat.message.read',
} as const;

// Re-exportados para que los handlers de meeting no necesiten importar
// directamente de chat-socket-events.ts — mismos DTOs, distinto namespace de eventos.
export type { MessageDto, MessageEditedDto, MessageDeletedDto, ReadReceiptDto };
