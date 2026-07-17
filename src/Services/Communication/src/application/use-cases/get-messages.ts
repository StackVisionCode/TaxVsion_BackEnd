import { Result, makeError } from '../../domain/shared/result.js';
import type { ConversationRepository } from '../ports/conversation-repository.js';
import type { MessageDto } from '../../contracts/socket/chat-socket-events.js';

/**
 * Paginacion cursor-based con dos modos mutuamente excluyentes:
 *   - `beforeUtc`: scrollback hacia atras, desc por createdAt (uso normal al
 *     abrir la conversacion / cargar mas historial).
 *   - `afterUtc`: backfill hacia adelante, asc por createdAt — el cliente
 *     reconecta un socket tras estar offline y pide todo lo que se perdio
 *     desde el `createdAtUtc` de su ultimo mensaje conocido.
 * Si se mandan ambos, `afterUtc` gana (es el caso de uso mas especifico).
 */
export interface GetMessagesQuery {
  readonly tenantId: string;
  readonly conversationId: string;
  readonly requesterUserId: string;
  readonly beforeUtc?: string;
  readonly afterUtc?: string;
  readonly take: number;
}

export interface GetMessagesResult {
  readonly items: readonly MessageDto[];
  readonly nextBeforeUtc: string | null;
  readonly nextAfterUtc: string | null;
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

  const afterUtc = query.afterUtc ? new Date(query.afterUtc) : undefined;
  const beforeUtc = !afterUtc && query.beforeUtc ? new Date(query.beforeUtc) : undefined;
  const listArgs: {
    tenantId: string;
    conversationId: string;
    take: number;
    beforeUtc?: Date;
    afterUtc?: Date;
  } = {
    tenantId: query.tenantId,
    conversationId: query.conversationId,
    take,
  };
  if (afterUtc !== undefined) listArgs.afterUtc = afterUtc;
  else if (beforeUtc !== undefined) listArgs.beforeUtc = beforeUtc;
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
    forwardedFromMessageId: m.forwardedFromMessageId,
    isEdited: m.isEdited,
    isDeleted: m.isDeleted,
    isPinned: m.isPinned,
    pinnedAtUtc: m.pinnedAtUtc ? m.pinnedAtUtc.toISOString() : null,
    pinnedByUserId: m.pinnedByUserId,
    createdAtUtc: m.createdAtUtc.toISOString(),
    editedAtUtc: m.editedAtUtc ? m.editedAtUtc.toISOString() : null,
  }));

  const hasMore = items.length === take;
  const lastItem = items[items.length - 1];
  const nextBeforeUtc = !afterUtc && hasMore && lastItem ? lastItem.createdAtUtc : null;
  const nextAfterUtc = afterUtc && hasMore && lastItem ? lastItem.createdAtUtc : null;

  return Result.ok({ items, nextBeforeUtc, nextAfterUtc });
}
