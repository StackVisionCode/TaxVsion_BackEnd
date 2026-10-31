import { z } from 'zod';

/**
 * Contratos socket para chat. Nomenclatura jerarquica `dominio.entidad.accion`
 * (§9D del plan). Cliente emite comandos con Idempotency-Key; server responde
 * via ack estructurado. Server emite eventos con envelope SocketEnvelope<T>.
 *
 * Los schemas Zod se USAN para validar payloads en el server (`safeParse` en
 * los handlers). NUNCA confiar en TypeScript en el boundary — el cliente puede
 * mandar cualquier cosa.
 */

// -------- Client -> Server --------

export const StartDirectConversationPayloadSchema = z.object({
  clientKey: z.string().min(1).max(128),
  recipientUserId: z.string().uuid(),
});
export type StartDirectConversationPayload = z.infer<typeof StartDirectConversationPayloadSchema>;

export const StartGroupConversationPayloadSchema = z.object({
  clientKey: z.string().min(1).max(128),
  title: z.string().min(1).max(120),
  // F8b (T-21): descripción opcional al crear.
  description: z.string().max(500).optional(),
  memberUserIds: z.array(z.string().uuid()).min(1).max(200),
});
export type StartGroupConversationPayload = z.infer<typeof StartGroupConversationPayloadSchema>;

export const AddGroupParticipantPayloadSchema = z.object({
  clientKey: z.string().min(1).max(128),
  conversationId: z.string().uuid(),
  newMemberUserId: z.string().uuid(),
});
export type AddGroupParticipantPayload = z.infer<typeof AddGroupParticipantPayloadSchema>;

export const RemoveGroupParticipantPayloadSchema = z.object({
  clientKey: z.string().min(1).max(128),
  conversationId: z.string().uuid(),
  // Omitido = el actor se quita a si mismo (salir del grupo).
  targetUserId: z.string().uuid().optional(),
});
export type RemoveGroupParticipantPayload = z.infer<typeof RemoveGroupParticipantPayloadSchema>;

/** F8b (T-21): nombre, descripción e imagen del grupo; campos ausentes no cambian, null borra. */
export const UpdateGroupInfoPayloadSchema = z.object({
  conversationId: z.string().uuid(),
  title: z.string().min(1).max(120).optional(),
  description: z.string().max(500).nullable().optional(),
  avatarFileId: z.string().uuid().nullable().optional(),
});
export type UpdateGroupInfoPayload = z.infer<typeof UpdateGroupInfoPayloadSchema>;

/** F8b (D-23): modo anuncio. */
export const SetGroupAdminsOnlyPayloadSchema = z.object({
  conversationId: z.string().uuid(),
  enabled: z.boolean(),
});
export type SetGroupAdminsOnlyPayload = z.infer<typeof SetGroupAdminsOnlyPayloadSchema>;

/** F8b (D-22): el Owner nombra o quita Admins. */
export const SetGroupMemberRolePayloadSchema = z.object({
  conversationId: z.string().uuid(),
  targetUserId: z.string().uuid(),
  role: z.enum(['Admin', 'Member']),
});
export type SetGroupMemberRolePayload = z.infer<typeof SetGroupMemberRolePayloadSchema>;

export const TransferGroupOwnershipPayloadSchema = z.object({
  conversationId: z.string().uuid(),
  targetUserId: z.string().uuid(),
});
export type TransferGroupOwnershipPayload = z.infer<typeof TransferGroupOwnershipPayloadSchema>;

/** ADD-05 H-6: silencio de notificaciones propio. */
export const SetConversationMutedPayloadSchema = z.object({
  conversationId: z.string().uuid(),
  muted: z.boolean(),
});
export type SetConversationMutedPayload = z.infer<typeof SetConversationMutedPayloadSchema>;

export const SendMessagePayloadSchema = z.object({
  clientKey: z.string().min(1).max(128),
  conversationId: z.string().uuid(),
  body: z.string().min(1).max(4000).optional(),
  attachmentFileId: z.string().uuid().optional(),
  replyToMessageId: z.string().uuid().optional(),
  // Nota de voz: metadata de audio calculada por el cliente al grabar (solo con attachmentFileId de audio).
  audioDurationMs: z.number().int().min(0).max(3_600_000).optional(),
  // Waveform: picos 0-100 (se capa a 128 barras para no inflar el payload/DB).
  audioWaveform: z.array(z.number().min(0).max(100)).max(128).optional(),
});
export type SendMessagePayload = z.infer<typeof SendMessagePayloadSchema>;

export const EditMessagePayloadSchema = z.object({
  clientKey: z.string().min(1).max(128),
  messageId: z.string().uuid(),
  body: z.string().min(1).max(4000),
});
export type EditMessagePayload = z.infer<typeof EditMessagePayloadSchema>;

export const DeleteMessagePayloadSchema = z.object({
  clientKey: z.string().min(1).max(128),
  messageId: z.string().uuid(),
});
export type DeleteMessagePayload = z.infer<typeof DeleteMessagePayloadSchema>;

export const MarkReadPayloadSchema = z.object({
  clientKey: z.string().min(1).max(128),
  conversationId: z.string().uuid(),
  lastReadMessageId: z.string().uuid(),
});
export type MarkReadPayload = z.infer<typeof MarkReadPayloadSchema>;

/**
 * Cotejo de ENTREGA (delivered): el receptor lo emite al RECIBIR mensajes por socket
 * SIN abrir la conversación (marca los 2 cotejos grises al emisor). No implica lectura;
 * abrir el chat dispara `mark_read` (2 azules) — son estados distintos a propósito.
 */
export const MarkDeliveredPayloadSchema = z.object({
  clientKey: z.string().min(1).max(128),
  conversationId: z.string().uuid(),
  upToMessageId: z.string().uuid(),
});
export type MarkDeliveredPayload = z.infer<typeof MarkDeliveredPayloadSchema>;

export const TypingPayloadSchema = z.object({
  conversationId: z.string().uuid(),
});
export type TypingPayload = z.infer<typeof TypingPayloadSchema>;

// Indicador "grabando una nota de voz…" — mismo shape que typing (el cliente re-emite start cada pocos
// segundos mientras graba para renovar el TTL del servidor, igual que typing renueva por tecleo).
export const RecordingPayloadSchema = z.object({
  conversationId: z.string().uuid(),
});
export type RecordingPayload = z.infer<typeof RecordingPayloadSchema>;

/**
 * Snapshot de presencia bajo demanda: el cliente pide el estado ACTUAL de unos userIds (los peers de
 * la conversación que abrió). `chat.presence.changed` solo se emite en TRANSICIONES, así que un peer
 * que ya estaba conectado antes de abrir el chat nunca se reflejaba (quedaba "Offline"). El server
 * responde emitiendo `chat.presence.changed` (Online/Offline) SOLO a este socket, reusando el pipeline.
 */
export const PresenceQueryPayloadSchema = z.object({
  userIds: z.array(z.string().uuid()).min(1).max(100),
});
export type PresenceQueryPayload = z.infer<typeof PresenceQueryPayloadSchema>;

// ---------- Fase Backend 9 — reactions / pin / forward ----------

export const AddReactionPayloadSchema = z.object({
  clientKey: z.string().min(1).max(128),
  messageId: z.string().uuid(),
  // El emoji viaja como codepoint literal (con variation selectors + skin
  // tones); no como shortcode ":thumbsup:" — el mapeo es responsabilidad del FE.
  emoji: z.string().min(1).max(16),
});
export type AddReactionPayload = z.infer<typeof AddReactionPayloadSchema>;

export const RemoveReactionPayloadSchema = AddReactionPayloadSchema;
export type RemoveReactionPayload = z.infer<typeof RemoveReactionPayloadSchema>;

export const PinMessagePayloadSchema = z.object({
  clientKey: z.string().min(1).max(128),
  messageId: z.string().uuid(),
});
export type PinMessagePayload = z.infer<typeof PinMessagePayloadSchema>;

export const UnpinMessagePayloadSchema = PinMessagePayloadSchema;
export type UnpinMessagePayload = z.infer<typeof UnpinMessagePayloadSchema>;

export const ForwardMessagePayloadSchema = z.object({
  clientKey: z.string().min(1).max(128),
  originMessageId: z.string().uuid(),
  targetConversationId: z.string().uuid(),
});
export type ForwardMessagePayload = z.infer<typeof ForwardMessagePayloadSchema>;

/**
 * Support — chat del AGENTE sobre un ticket. La conversación Support vive en el tenant del CLIENTE y
 * tiene 2 participantes fijos: el cliente + un placeholder "Support Team" (userId = id del tenant
 * Platform). El agente real NO es participante y está en otro tenant, así que NO puede usar
 * `chat.message.send` (escopa por su tenant + exige isParticipant). Estos comandos resuelven el ticket,
 * validan acceso de agente y publican/leen la conversación cross-tenant AUTORÍA del placeholder
 * ("Support Team", anonimiza al agente). La salida reusa `chat.message.new`/`chat.message.delivered`.
 */
export const SupportAgentSendMessagePayloadSchema = z.object({
  clientKey: z.string().min(1).max(128),
  ticketId: z.string().uuid(),
  body: z.string().min(1).max(4000).optional(),
  attachmentFileId: z.string().uuid().optional(),
  replyToMessageId: z.string().uuid().optional(),
  audioDurationMs: z.number().int().min(0).max(3_600_000).optional(),
  audioWaveform: z.array(z.number().min(0).max(100)).max(128).optional(),
});
export type SupportAgentSendMessagePayload = z.infer<typeof SupportAgentSendMessagePayloadSchema>;

/** El agente se une a la sala de la conversación del ticket para recibir mensajes/typing en vivo. */
export const SupportAgentJoinPayloadSchema = z.object({
  ticketId: z.string().uuid(),
});
export type SupportAgentJoinPayload = z.infer<typeof SupportAgentJoinPayloadSchema>;

// -------- Server -> Client --------

export interface ConversationSummaryDto {
  id: string;
  kind: 'Direct' | 'Group' | 'Support' | 'Meeting';
  title: string | null;
  lastMessageAtUtc: string | null;
  updatedAtUtc: string;
}

/** F8d (T-26): reacciones agregadas de un mensaje (historial). `userIds` = quién, para el tooltip con foto. */
export interface MessageReactionSummaryDto {
  emoji: string;
  count: number;
  userIds: string[];
}

export interface MessageDto {
  id: string;
  conversationId: string;
  senderId: string;
  senderDisplayName: string;
  /**
   * F5 avatares — foto del remitente, solo en el historial (`GET .../messages`). Ausente en los eventos
   * en vivo (`chat.message.new`): el front usa la de los participantes. null = sin foto.
   */
  senderAvatarFileId?: string | null;
  /**
   * F8a (T-10): tipo de la conversación en `chat.message.new`, para que cada módulo tome lo suyo (Chat =
   * Direct/Group, Support, sala = Meeting). Ausente en el historial (ya se pidió por conversación).
   */
  conversationKind?: 'Direct' | 'Group' | 'Support' | 'Meeting';
  /** F8a: meeting dueño cuando `conversationKind` es Meeting. */
  meetingId?: string | null;
  kind: 'Text' | 'Attachment' | 'System';
  body: string | null;
  attachmentFileId: string | null;
  replyToMessageId: string | null;
  /** Fase Backend 9 — set en messages creados via forward-message.ts. */
  forwardedFromMessageId: string | null;
  isEdited: boolean;
  /** F8: cuántas veces se editó (tope por política). */
  editCount: number;
  isDeleted: boolean;
  /** Fase Backend 9 — snapshot del estado de pin. Cambia via MessagePinned/Unpinned events. */
  isPinned: boolean;
  pinnedAtUtc: string | null;
  pinnedByUserId: string | null;
  createdAtUtc: string;
  editedAtUtc: string | null;
  /**
   * Cotejos del EMISOR sobre SU propio mensaje (agregado del otro/los otros participantes):
   * `deliveredAtUtc` = entregado (2 grises), `readAtUtc` = leído (2 azules). `null` = solo enviado
   * (1 gris). En `chat.message.new` llegan null (recién creado); se pueblan al cargar historial
   * y avanzan en vivo por `chat.message.delivered` / `chat.message.read`. Para mensajes ajenos, null.
   */
  deliveredAtUtc: string | null;
  readAtUtc: string | null;
  /** Nota de voz (attachment de audio): duración en ms y waveform (picos 0-100). Null en cualquier otro mensaje. */
  audioDurationMs: number | null;
  audioWaveform: number[] | null;
  /** F7 (D-27): transcript de la nota de voz y su estado (null en cualquier otro mensaje). */
  transcriptText?: string | null;
  transcriptState?: MessageTranscriptState | null;
  /** F8d (T-26): solo en el historial; en vivo se suman con `chat.message.reaction.*`. */
  reactions?: MessageReactionSummaryDto[];
}

export type MessageTranscriptState = 'NotRequested' | 'Processing' | 'Ready' | 'Failed' | 'Skipped';

/** F7 (D-27): el transcript de una nota de voz cambio de estado (a la sala de la conversacion). */
export interface MessageTranscriptUpdatedDto {
  conversationId: string;
  messageId: string;
  transcriptState: MessageTranscriptState;
  transcriptText: string | null;
}

export interface MessageEditedDto {
  messageId: string;
  conversationId: string;
  body: string;
  editedAtUtc: string;
  /** F8: total de ediciones tras esta (el front oculta "Edit" al llegar al tope). */
  editCount: number;
}

export interface MessageDeletedDto {
  messageId: string;
  conversationId: string;
  deletedAtUtc: string;
}

export interface TypingDto {
  conversationId: string;
  userId: string;
  displayName: string;
}

/** "grabando una nota de voz…" — mismo shape que TypingDto. */
export interface RecordingDto {
  conversationId: string;
  userId: string;
  displayName: string;
}

export interface ReadReceiptDto {
  conversationId: string;
  userId: string;
  lastReadMessageId: string;
  readAtUtc: string;
}

/** Cotejo de entrega (2 grises). `userId` = quien recibió; el emisor lo pinta sobre sus mensajes ≤ upToMessageId. */
export interface DeliveryReceiptDto {
  conversationId: string;
  userId: string;
  upToMessageId: string;
  deliveredAtUtc: string;
}

/**
 * Fase A1/A2 — presencia enriquecida: `status` reemplaza al viejo `online:
 * boolean` (Offline/Online/Busy). `busyReason` solo tiene valor cuando
 * status es 'Busy' (deriva de una Call o un Meeting activos).
 */
export interface PresenceChangedDto {
  userId: string;
  status: 'Online' | 'Busy' | 'Offline';
  busyReason: 'Call' | 'Meeting' | null;
  changedAtUtc: string;
}

export interface AttachmentFlaggedDto {
  messageId: string;
  conversationId: string;
  fileId: string;
  // PendingReview: en revisión de contenido; Available: el revisor lo aprobó y vuelve a mostrarse.
  status: 'Infected' | 'Deleted' | 'BlockedByPolicy' | 'PendingReview' | 'Available';
  flaggedAtUtc: string;
}

// ---------- Fase Backend 9 — reactions / pin / forward (S→C) ----------

export interface MessageReactionAddedDto {
  messageId: string;
  conversationId: string;
  userId: string;
  emoji: string;
  addedAtUtc: string;
}

export interface MessageReactionRemovedDto {
  messageId: string;
  conversationId: string;
  userId: string;
  emoji: string;
  removedAtUtc: string;
}

export interface MessagePinnedDto {
  messageId: string;
  conversationId: string;
  pinnedByUserId: string;
  pinnedAtUtc: string;
}

export interface MessageUnpinnedDto {
  messageId: string;
  conversationId: string;
  unpinnedByUserId: string;
  unpinnedAtUtc: string;
}

export interface ConversationCreatedDto {
  id: string;
  kind: 'Direct' | 'Group' | 'Support' | 'Meeting';
  title: string | null;
  createdByUserId: string;
  createdAtUtc: string;
}

export interface ConversationParticipantAddedDto {
  conversationId: string;
  addedByUserId: string;
  newParticipantUserId: string;
  newParticipantDisplayName: string;
  /** F5 avatares — foto del nuevo miembro; ausente si no se pudo resolver, null = sin foto. */
  newParticipantAvatarFileId?: string | null;
  /** F8b: cliente vs staff (los clientes nunca administran). */
  newParticipantActorType?: string;
  addedAtUtc: string;
}

/** F8b (D-22): cambio de rol en un grupo (promoción, revocación, transferencia o sucesión automática). */
export interface ConversationParticipantRoleChangedDto {
  conversationId: string;
  userId: string;
  role: 'Owner' | 'Admin' | 'Member';
  /** null = el sistema (sucesión por salida o baja). */
  changedByUserId: string | null;
  changedAtUtc: string;
}

export interface ConversationParticipantRemovedDto {
  conversationId: string;
  removedByUserId: string;
  removedParticipantUserId: string;
  reason: 'Left' | 'Kicked';
  removedAtUtc: string;
}

/**
 * F8: cambio de la conversación para quien la tiene abierta. Campos ausentes = no cambiaron. 8a: `isArchived`
 * (meeting terminado / ticket cerrado → solo lectura); 8b suma nombre, descripción, imagen y "solo admins".
 */
export interface ConversationUpdatedDto {
  conversationId: string;
  kind: 'Direct' | 'Group' | 'Support' | 'Meeting';
  isArchived?: boolean;
  title?: string | null;
  description?: string | null;
  avatarFileId?: string | null;
  adminsOnlyWrite?: boolean;
  /** Solo a las pestañas del propio usuario (silencio propio). */
  isMuted?: boolean;
}

export const ChatSocketEvents = {
  // c -> s
  StartDirectConversation: 'chat.conversation.start_direct',
  StartGroupConversation: 'chat.conversation.start_group',
  AddGroupParticipant: 'chat.conversation.add_participant',
  RemoveGroupParticipant: 'chat.conversation.remove_participant',
  SendMessage: 'chat.message.send',
  EditMessage: 'chat.message.edit',
  DeleteMessage: 'chat.message.delete',
  MarkRead: 'chat.message.mark_read',
  MarkDelivered: 'chat.message.mark_delivered',
  TypingStart: 'chat.typing.start',
  TypingStop: 'chat.typing.stop',
  RecordingStart: 'chat.recording.start',
  RecordingStop: 'chat.recording.stop',
  PresenceQuery: 'chat.presence.query',
  AddReaction: 'chat.message.reaction.add',
  RemoveReaction: 'chat.message.reaction.remove',
  PinMessage: 'chat.message.pin',
  UnpinMessage: 'chat.message.unpin',
  ForwardMessage: 'chat.message.forward',
  // F8b: administración de grupos y silencio propio (c -> s).
  UpdateGroupInfo: 'chat.conversation.update_info',
  SetGroupAdminsOnly: 'chat.conversation.set_admins_only',
  SetGroupMemberRole: 'chat.conversation.set_role',
  TransferGroupOwnership: 'chat.conversation.transfer_ownership',
  SetConversationMuted: 'chat.conversation.mute',
  // Support — chat de agente (c -> s). La salida reusa MessageNew/MessageDelivered.
  SupportAgentJoin: 'support.ticket.join',
  SupportAgentSendMessage: 'support.message.send',

  // s -> c
  MessageNew: 'chat.message.new',
  MessageEdited: 'chat.message.edited',
  MessageDeleted: 'chat.message.deleted',
  MessageRead: 'chat.message.read',
  MessageDelivered: 'chat.message.delivered',
  TypingStarted: 'chat.typing.started',
  TypingStopped: 'chat.typing.stopped',
  RecordingStarted: 'chat.recording.started',
  RecordingStopped: 'chat.recording.stopped',
  ConversationCreated: 'chat.conversation.created',
  ConversationParticipantAdded: 'chat.conversation.participant_added',
  ConversationParticipantRemoved: 'chat.conversation.participant_removed',
  // F8: archivado (8a) y datos del grupo (8b).
  ConversationUpdated: 'chat.conversation.updated',
  ConversationParticipantRoleChanged: 'chat.conversation.participant_role_changed',
  PresenceChanged: 'chat.presence.changed',
  AttachmentFlagged: 'chat.message.attachment_flagged',
  MessageReactionAdded: 'chat.message.reaction.added',
  MessageReactionRemoved: 'chat.message.reaction.removed',
  MessagePinned: 'chat.message.pinned',
  MessageUnpinned: 'chat.message.unpinned',
  MessageTranscriptUpdated: 'chat.message.transcript_updated',
  // Forward reusa MessageNew para el target conversation (es un mensaje nuevo).
} as const;
