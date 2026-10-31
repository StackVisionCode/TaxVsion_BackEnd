import { Result, makeError } from '../../domain/shared/result.js';
import type { ConversationRepository } from '../ports/conversation-repository.js';
import type { MessageRepository } from '../ports/message-repository.js';

export interface ListMessageEditsQuery {
  readonly tenantId: string;
  readonly conversationId: string;
  readonly messageId: string;
  readonly requesterUserId: string;
}

export interface MessageEditVersionDto {
  readonly revision: number;
  readonly body: string;
  /** Cuándo se reemplazó este texto (la edición que lo cambió). */
  readonly replacedAtUtc: string;
}

export interface MessageEditsResult {
  readonly messageId: string;
  readonly currentBody: string;
  readonly editedAtUtc: string | null;
  /** Versiones anteriores, de la más vieja (el original) a la más nueva. */
  readonly versions: readonly MessageEditVersionDto[];
}

/**
 * F8: "Edited" → versiones anteriores. Transparencia para los dos lados (estilo iMessage/Signal): quien recibe
 * puede ver qué decía antes. Solo participantes del chat y nunca de un mensaje borrado.
 */
export async function listMessageEdits(
  query: ListMessageEditsQuery,
  deps: { conversations: Pick<ConversationRepository, 'findById'>; messages: Pick<MessageRepository, 'findById' | 'listEdits'> },
): Promise<Result<MessageEditsResult>> {
  const conversation = await deps.conversations.findById(query.tenantId, query.conversationId, 0);
  if (!conversation) return Result.fail(makeError('Chat.Conversation.NotFound', 'Conversation not found.'));
  if (!conversation.isParticipant(query.requesterUserId)) {
    return Result.fail(makeError('Chat.Conversation.NotParticipant', 'User is not a participant.'));
  }
  const message = await deps.messages.findById(query.tenantId, query.messageId);
  const snapshot = message?.toSnapshot();
  if (!snapshot || snapshot.conversationId !== query.conversationId || snapshot.isDeleted) {
    return Result.fail(makeError('Chat.Message.NotFound', 'Message not found.'));
  }
  const edits = await deps.messages.listEdits(query.tenantId, query.messageId);
  return Result.ok({
    messageId: snapshot.id,
    currentBody: snapshot.body ?? '',
    editedAtUtc: snapshot.editedAtUtc?.toISOString() ?? null,
    versions: edits.map((e) => ({ revision: e.revision, body: e.previousBody, replacedAtUtc: e.editedAtUtc.toISOString() })),
  });
}
