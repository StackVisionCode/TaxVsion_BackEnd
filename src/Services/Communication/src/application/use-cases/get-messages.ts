import { Result, makeError } from '../../domain/shared/result.js';
import type { ConversationRepository } from '../ports/conversation-repository.js';
import type { MessageDto } from '../../contracts/socket/chat-socket-events.js';

/**
 * Paginacion cursor-based: `before` = ISO-8601 del cursor. Se devuelven los
 * mensajes anteriores a esa fecha ordenados desc por createdAt. El cliente
 * pinta al reves al mostrar.
 */
export interface GetMessagesQuery {
  readonly tenantId: string;
  readonly conversationId: string;
  readonly requesterUserId: string;
  readonly beforeUtc?: string;
  readonly take: number;
}

export interface GetMessagesResult {
  readonly items: readonly MessageDto[];
  readonly nextBeforeUtc: string | null;
}

export async function getMessages(
  query: GetMessagesQuery,
  deps: { conversations: ConversationRepository },
): Promise<Result<GetMessagesResult>> {
  const take = Math.min(Math.max(query.take, 1), 100);
  const conversation = await deps.conversations.findById(query.tenantId, query.conversationId, 0);
  if (!conversation) {
    return Result.fail(makeError('Chat.Conversation.NotFound', 'Conversation not found.'));
  }
  if (!conversation.isParticipant(query.requesterUserId)) {
    return Result.fail(makeError('Chat.Conversation.NotParticipant', 'User is not a participant.'));
  }

  const beforeUtc = query.beforeUtc ? new Date(query.beforeUtc) : undefined;
  const listArgs: {
    tenantId: string;
    conversationId: string;
    take: number;
    beforeUtc?: Date;
  } = {
    tenantId: query.tenantId,
    conversationId: query.conversationId,
    take,
  };
  if (beforeUtc !== undefined) listArgs.beforeUtc = beforeUtc;
  const messages = await deps.conversations.listMessages(listArgs);

  const items: MessageDto[] = messages.map((m) => ({
    id: m.id,
    conversationId: m.conversationId,
    senderId: m.senderId,
    senderDisplayName: m.senderDisplayName,
    kind: m.kind,
    body: m.isDeleted ? null : m.body,
    attachmentFileId: m.isDeleted ? null : m.attachmentFileId,
    replyToMessageId: m.replyToMessageId,
    isEdited: m.isEdited,
    isDeleted: m.isDeleted,
    createdAtUtc: m.createdAtUtc.toISOString(),
    editedAtUtc: m.editedAtUtc ? m.editedAtUtc.toISOString() : null,
  }));

  const nextBeforeUtc =
    items.length === take && items[items.length - 1]
      ? items[items.length - 1]!.createdAtUtc
      : null;

  return Result.ok({ items, nextBeforeUtc });
}
