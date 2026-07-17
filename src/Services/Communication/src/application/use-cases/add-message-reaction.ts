import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../../domain/shared/result.js';
import { MessageReaction } from '../../domain/conversations/message-reaction.js';
import type { MessageRepository } from '../ports/message-repository.js';
import type { ConversationRepository } from '../ports/conversation-repository.js';
import type { RealtimeEmitter } from '../ports/realtime-emitter.js';
import {
  ChatSocketEvents,
  type MessageReactionAddedDto,
} from '../../contracts/socket/chat-socket-events.js';

export interface AddMessageReactionCommand {
  readonly tenantId: string;
  readonly correlationId: string;
  readonly messageId: string;
  readonly userId: string;
  readonly emoji: string;
}

export interface AddMessageReactionDeps {
  readonly messages: MessageRepository;
  readonly conversations: ConversationRepository;
  readonly emitter: RealtimeEmitter;
}

/**
 * Fase Backend 9 — cualquier participante activo de la conversation puede
 * reaccionar. El unique (MessageId, UserId, Emoji) en Prisma hace no-op el
 * segundo click del cliente sobre el mismo emoji — no necesita idempotency
 * store separado. Emite el socket event solo si la reaction fue realmente
 * nueva (evita duplicate broadcasts en el room del conversation).
 */
export async function addMessageReaction(
  cmd: AddMessageReactionCommand,
  deps: AddMessageReactionDeps,
): Promise<Result<{ wasNew: boolean }>> {
  const message = await deps.messages.findById(cmd.tenantId, cmd.messageId);
  if (!message) return Result.fail(makeError('Chat.Message.NotFound', 'Message not found.'));

  const messageSnapshot = message.toSnapshot();
  if (messageSnapshot.isDeleted) {
    return Result.fail(makeError('Chat.Reaction.MessageDeleted', 'Cannot react to a deleted message.'));
  }

  // Cargar la conversation sin mensajes recientes solo para verificar
  // participacion. Barato: es la fila raiz + participants.
  const conversation = await deps.conversations.findById(cmd.tenantId, messageSnapshot.conversationId);
  if (!conversation) return Result.fail(makeError('Chat.Conversation.NotFound', 'Conversation not found.'));
  if (!conversation.isParticipant(cmd.userId)) {
    return Result.fail(makeError('Chat.Conversation.NotParticipant', 'User is not a participant.'));
  }

  const now = new Date();
  const reactionResult = MessageReaction.create({
    messageId: cmd.messageId,
    tenantId: cmd.tenantId,
    userId: cmd.userId,
    emoji: cmd.emoji,
    now,
  });
  if (!reactionResult.isSuccess) return Result.fail(reactionResult.error);

  const { wasNew } = await deps.messages.addReaction(reactionResult.value);
  if (!wasNew) return Result.ok({ wasNew: false });

  const dto: MessageReactionAddedDto = {
    messageId: cmd.messageId,
    conversationId: messageSnapshot.conversationId,
    userId: cmd.userId,
    emoji: reactionResult.value.emoji,
    addedAtUtc: now.toISOString(),
  };
  deps.emitter.emitToConversation({
    tenantId: cmd.tenantId,
    conversationId: messageSnapshot.conversationId,
    event: ChatSocketEvents.MessageReactionAdded,
    envelope: {
      eventId: randomUUID(),
      correlationId: cmd.correlationId,
      emittedAtUtc: now.toISOString(),
      payload: dto,
    },
  });
  return Result.ok({ wasNew: true });
}
