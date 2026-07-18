import { Result, makeError } from '../../domain/shared/result.js';
import type { ConversationRepository } from '../ports/conversation-repository.js';
import type { MessageRepository } from '../ports/message-repository.js';
import type { ReadReceiptDto } from '../../contracts/socket/chat-socket-events.js';

/**
 * Marca todos los mensajes de una conversacion como leidos hasta
 * `lastReadMessageId` para el usuario. Batch operation en un solo statement
 * (cierra el N+1 del legacy `mark_messages_read` por-mensaje).
 *
 * No requiere idempotencia explicita: la operacion es intrinsicamente
 * idempotente (WHERE ReadAtUtc IS NULL).
 */
export interface MarkMessagesReadCommand {
  readonly tenantId: string;
  readonly conversationId: string;
  readonly userUserId: string;
  readonly lastReadMessageId: string;
}

export interface MarkMessagesReadResult {
  readonly receipt: ReadReceiptDto;
  readonly markedCount: number;
}

export interface MarkMessagesReadDeps {
  readonly conversations: ConversationRepository;
  readonly messages: MessageRepository;
}

export async function markMessagesRead(
  command: MarkMessagesReadCommand,
  deps: MarkMessagesReadDeps,
): Promise<Result<MarkMessagesReadResult>> {
  const conversation = await deps.conversations.findById(command.tenantId, command.conversationId, 0);
  if (!conversation) {
    return Result.fail(makeError('Chat.Conversation.NotFound', 'Conversation not found.'));
  }
  if (!conversation.isParticipant(command.userUserId)) {
    return Result.fail(makeError('Chat.Conversation.NotParticipant', 'User is not a participant.'));
  }

  const now = new Date();
  const markResult = conversation.markRead({
    participantUserId: command.userUserId,
    lastReadMessageId: command.lastReadMessageId,
    now,
  });
  if (!markResult.isSuccess) return Result.fail(markResult.error);

  await deps.conversations.save(conversation);
  const { markedCount } = await deps.messages.markBatchRead({
    tenantId: command.tenantId,
    conversationId: command.conversationId,
    userId: command.userUserId,
    lastReadMessageId: command.lastReadMessageId,
    now,
  });

  return Result.ok({
    receipt: {
      conversationId: command.conversationId,
      userId: command.userUserId,
      lastReadMessageId: command.lastReadMessageId,
      readAtUtc: now.toISOString(),
    },
    markedCount,
  });
}
