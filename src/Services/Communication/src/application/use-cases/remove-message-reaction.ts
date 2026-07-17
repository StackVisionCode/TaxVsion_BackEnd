import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../../domain/shared/result.js';
import type { MessageRepository } from '../ports/message-repository.js';
import type { RealtimeEmitter } from '../ports/realtime-emitter.js';
import {
  ChatSocketEvents,
  type MessageReactionRemovedDto,
} from '../../contracts/socket/chat-socket-events.js';

export interface RemoveMessageReactionCommand {
  readonly tenantId: string;
  readonly correlationId: string;
  readonly messageId: string;
  readonly userId: string;
  readonly emoji: string;
}

export interface RemoveMessageReactionDeps {
  readonly messages: MessageRepository;
  readonly emitter: RealtimeEmitter;
}

/**
 * Un usuario solo puede quitar SU propia reaction — el delete lleva su userId
 * en el WHERE, un tercero no puede quitar la reaccion de otro. `wasPresent`
 * false = idempotente (segundo click = no-op silencioso, no emite).
 */
export async function removeMessageReaction(
  cmd: RemoveMessageReactionCommand,
  deps: RemoveMessageReactionDeps,
): Promise<Result<{ wasPresent: boolean }>> {
  const message = await deps.messages.findById(cmd.tenantId, cmd.messageId);
  if (!message) return Result.fail(makeError('Chat.Message.NotFound', 'Message not found.'));

  const messageSnapshot = message.toSnapshot();
  const { wasPresent } = await deps.messages.removeReaction({
    tenantId: cmd.tenantId,
    messageId: cmd.messageId,
    userId: cmd.userId,
    emoji: cmd.emoji.trim(),
  });
  if (!wasPresent) return Result.ok({ wasPresent: false });

  const now = new Date();
  const dto: MessageReactionRemovedDto = {
    messageId: cmd.messageId,
    conversationId: messageSnapshot.conversationId,
    userId: cmd.userId,
    emoji: cmd.emoji.trim(),
    removedAtUtc: now.toISOString(),
  };
  deps.emitter.emitToConversation({
    tenantId: cmd.tenantId,
    conversationId: messageSnapshot.conversationId,
    event: ChatSocketEvents.MessageReactionRemoved,
    envelope: {
      eventId: randomUUID(),
      correlationId: cmd.correlationId,
      emittedAtUtc: now.toISOString(),
      payload: dto,
    },
  });
  return Result.ok({ wasPresent: true });
}
