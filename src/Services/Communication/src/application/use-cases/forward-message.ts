import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../../domain/shared/result.js';
import { Message } from '../../domain/conversations/message.js';
import type { MessageRepository } from '../ports/message-repository.js';
import type { ConversationRepository } from '../ports/conversation-repository.js';
import type { IdempotencyStore } from '../ports/idempotency-store.js';
import type { RealtimeEmitter } from '../ports/realtime-emitter.js';
import type { UserDirectoryRepository } from '../ports/user-directory-repository.js';
import { resolveDisplayName } from '../../api/socket/handlers/resolve-display-name.js';
import {
  ChatSocketEvents,
  type MessageDto,
} from '../../contracts/socket/chat-socket-events.js';
import { messageSnapshotToDto } from './chat-mappers.js';

export interface ForwardMessageCommand {
  readonly tenantId: string;
  readonly correlationId: string;
  readonly clientKey: string;
  readonly originMessageId: string;
  readonly targetConversationId: string;
  readonly forwarderUserId: string;
}

export interface ForwardMessageResult {
  readonly message: MessageDto;
}

export interface ForwardMessageDeps {
  readonly messages: MessageRepository;
  readonly conversations: ConversationRepository;
  readonly idempotency: IdempotencyStore;
  readonly emitter: RealtimeEmitter;
  readonly userDirectory: UserDirectoryRepository;
}

/**
 * Fase Backend 9 — copia el body/attachment del mensaje origen a un mensaje
 * NUEVO en la conversation destino. El forwarder debe ser participante activo
 * de la conversation DESTINO (no de la origen — puedes reenviar un mensaje
 * de un grupo del que ya no formas parte, siempre que aun tengas el id).
 *
 * Cross-tenant: prohibido — el origen se rechaza si su tenantId difiere. Los
 * grupos multi-tenant no estan soportados en el modelo hoy.
 *
 * Idempotente por `clientKey` (mismo patron que send-message).
 */
export async function forwardMessage(
  cmd: ForwardMessageCommand,
  deps: ForwardMessageDeps,
): Promise<Result<ForwardMessageResult>> {
  const reservation = await deps.idempotency.tryReserve<ForwardMessageResult>({
    tenantId: cmd.tenantId,
    userId: cmd.forwarderUserId,
    scope: 'chat.message.forward',
    clientKey: cmd.clientKey,
    ttlSeconds: 60,
  });
  if (reservation.status === 'replay') return Result.ok(reservation.payload);

  const release = () =>
    deps.idempotency.release({
      tenantId: cmd.tenantId,
      userId: cmd.forwarderUserId,
      scope: 'chat.message.forward',
      clientKey: cmd.clientKey,
      token: reservation.token,
    });

  const origin = await deps.messages.findById(cmd.tenantId, cmd.originMessageId);
  if (!origin) {
    await release();
    return Result.fail(makeError('Chat.Message.NotFound', 'Origin message not found.'));
  }
  const originSnapshot = origin.toSnapshot();

  const targetConversation = await deps.conversations.findById(cmd.tenantId, cmd.targetConversationId);
  if (!targetConversation) {
    await release();
    return Result.fail(makeError('Chat.Conversation.NotFound', 'Target conversation not found.'));
  }
  if (!targetConversation.isParticipant(cmd.forwarderUserId)) {
    await release();
    return Result.fail(makeError('Chat.Conversation.NotParticipant', 'User is not a participant of the target conversation.'));
  }

  const forwarderDisplayName = await resolveDisplayName(deps.userDirectory, cmd.forwarderUserId);

  const createResult = Message.createForwarded({
    conversationId: cmd.targetConversationId,
    tenantId: cmd.tenantId,
    forwarderUserId: cmd.forwarderUserId,
    forwarderDisplayName,
    origin: originSnapshot,
  });
  if (!createResult.isSuccess) {
    await release();
    return Result.fail(createResult.error);
  }

  await deps.messages.insertForwarded(cmd.tenantId, createResult.value);

  const dto = messageSnapshotToDto(createResult.value.toSnapshot());
  deps.emitter.emitToConversation({
    tenantId: cmd.tenantId,
    conversationId: cmd.targetConversationId,
    event: ChatSocketEvents.MessageNew,
    envelope: {
      eventId: randomUUID(),
      correlationId: cmd.correlationId,
      emittedAtUtc: dto.createdAtUtc,
      payload: dto,
    },
  });

  const result: ForwardMessageResult = { message: dto };
  await deps.idempotency.commit({
    tenantId: cmd.tenantId,
    userId: cmd.forwarderUserId,
    scope: 'chat.message.forward',
    clientKey: cmd.clientKey,
    payload: result,
    token: reservation.token,
  });
  return Result.ok(result);
}
