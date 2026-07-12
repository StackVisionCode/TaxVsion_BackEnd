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

export const SendMessagePayloadSchema = z.object({
  clientKey: z.string().min(1).max(128),
  conversationId: z.string().uuid(),
  body: z.string().min(1).max(4000).optional(),
  attachmentFileId: z.string().uuid().optional(),
  replyToMessageId: z.string().uuid().optional(),
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

export const TypingPayloadSchema = z.object({
  conversationId: z.string().uuid(),
});
export type TypingPayload = z.infer<typeof TypingPayloadSchema>;

// -------- Server -> Client --------

export interface ConversationSummaryDto {
  id: string;
  kind: 'Direct' | 'Group' | 'Support' | 'Meeting';
  title: string | null;
  lastMessageAtUtc: string | null;
  updatedAtUtc: string;
}

export interface MessageDto {
  id: string;
  conversationId: string;
  senderId: string;
  senderDisplayName: string;
  kind: 'Text' | 'Attachment' | 'System';
  body: string | null;
  attachmentFileId: string | null;
  replyToMessageId: string | null;
  isEdited: boolean;
  isDeleted: boolean;
  createdAtUtc: string;
  editedAtUtc: string | null;
}

export interface MessageEditedDto {
  messageId: string;
  conversationId: string;
  body: string;
  editedAtUtc: string;
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

export interface ReadReceiptDto {
  conversationId: string;
  userId: string;
  lastReadMessageId: string;
  readAtUtc: string;
}

export interface PresenceChangedDto {
  userId: string;
  online: boolean;
  changedAtUtc: string;
}

export interface AttachmentFlaggedDto {
  messageId: string;
  conversationId: string;
  fileId: string;
  status: 'Infected' | 'Deleted' | 'BlockedByPolicy';
  flaggedAtUtc: string;
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
  addedAtUtc: string;
}

export interface ConversationParticipantRemovedDto {
  conversationId: string;
  removedByUserId: string;
  removedParticipantUserId: string;
  reason: 'Left' | 'Kicked';
  removedAtUtc: string;
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
  TypingStart: 'chat.typing.start',
  TypingStop: 'chat.typing.stop',

  // s -> c
  MessageNew: 'chat.message.new',
  MessageEdited: 'chat.message.edited',
  MessageDeleted: 'chat.message.deleted',
  MessageRead: 'chat.message.read',
  TypingStarted: 'chat.typing.started',
  TypingStopped: 'chat.typing.stopped',
  ConversationCreated: 'chat.conversation.created',
  ConversationParticipantAdded: 'chat.conversation.participant_added',
  ConversationParticipantRemoved: 'chat.conversation.participant_removed',
  PresenceChanged: 'chat.presence.changed',
  AttachmentFlagged: 'chat.message.attachment_flagged',
} as const;
