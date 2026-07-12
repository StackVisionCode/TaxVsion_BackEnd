import type {
  Conversation as PrismaConversation,
  ConversationParticipant as PrismaParticipant,
  Message as PrismaMessage,
} from '@prisma/client';
import {
  Conversation,
  type ConversationSnapshot,
} from '../../domain/conversations/conversation.js';
import type { ConversationParticipantSnapshot } from '../../domain/conversations/conversation-participant.js';
import type { MessageSnapshot } from '../../domain/conversations/message.js';
import { isConversationKind } from '../../domain/conversations/conversation-kind.js';
import { isMessageKind } from '../../domain/conversations/message-kind.js';

/**
 * Mappers Prisma <-> dominio. Concentrados en un solo archivo para que
 * cualquier cambio de schema tenga un unico lugar donde tocar.
 */

export function toDomainParticipant(row: PrismaParticipant): ConversationParticipantSnapshot {
  return {
    id: row.Id,
    conversationId: row.ConversationId,
    tenantId: row.TenantId,
    userId: row.UserId,
    displayName: row.DisplayName,
    actorType: row.ActorType,
    role: row.Role === 'Owner' ? 'Owner' : 'Member',
    isPrimaryPreparer: row.IsPrimaryPreparer,
    isMuted: row.IsMuted,
    isRemoved: row.IsRemoved,
    joinedAtUtc: row.JoinedAtUtc,
    removedAtUtc: row.RemovedAtUtc,
    lastReadAtUtc: row.LastReadAtUtc,
    lastReadMessageId: row.LastReadMessageId,
  };
}

export function toDomainMessage(row: PrismaMessage): MessageSnapshot {
  if (!isMessageKind(row.Kind)) {
    throw new Error(`Corrupted Message row: unknown Kind '${row.Kind}' (id=${row.Id})`);
  }
  return {
    id: row.Id,
    conversationId: row.ConversationId,
    tenantId: row.TenantId,
    senderId: row.SenderId,
    senderDisplayName: row.SenderDisplayName,
    kind: row.Kind,
    body: row.Body,
    attachmentFileId: row.AttachmentFileId,
    replyToMessageId: row.ReplyToMessageId,
    isEdited: row.IsEdited,
    isDeleted: row.IsDeleted,
    deletedAtUtc: row.DeletedAtUtc,
    createdAtUtc: row.CreatedAtUtc,
    editedAtUtc: row.EditedAtUtc,
  };
}

export function toDomainConversation(
  row: PrismaConversation,
  participants: PrismaParticipant[],
  recentMessages: PrismaMessage[],
): Conversation {
  if (!isConversationKind(row.Kind)) {
    throw new Error(`Corrupted Conversation row: unknown Kind '${row.Kind}' (id=${row.Id})`);
  }
  const snapshot: ConversationSnapshot = {
    id: row.Id,
    tenantId: row.TenantId,
    kind: row.Kind,
    title: row.Title,
    uniquenessKey: row.UniquenessKey,
    isArchived: row.IsArchived,
    lastMessageAtUtc: row.LastMessageAtUtc,
    createdByUserId: row.CreatedByUserId,
    createdAtUtc: row.CreatedAtUtc,
    updatedAtUtc: row.UpdatedAtUtc,
    participants: participants.map(toDomainParticipant),
    recentMessages: recentMessages.map(toDomainMessage),
  };
  return Conversation.rehydrate(snapshot);
}
