import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../../domain/shared/result.js';
import type { MessageRepository } from '../ports/message-repository.js';
import type { IdempotencyStore } from '../ports/idempotency-store.js';
import type { IntegrationEventPublisher } from '../ports/integration-event-publisher.js';
import { ChatEventTypes, type MessageDeletedEvent } from '../../contracts/events/chat-events.js';
import type { MessageDeletedDto } from '../../contracts/socket/chat-socket-events.js';

export interface DeleteMessageCommand {
  readonly tenantId: string;
  readonly correlationId: string;
  readonly clientKey: string;
  readonly messageId: string;
  readonly actorUserId: string;
  readonly actorCanModerate: boolean;
}

export interface DeleteMessageResult {
  readonly deleted: MessageDeletedDto;
}

export interface DeleteMessageDeps {
  readonly messages: MessageRepository;
  readonly idempotency: IdempotencyStore;
  readonly publisher: IntegrationEventPublisher;
}

export async function deleteMessage(
  command: DeleteMessageCommand,
  deps: DeleteMessageDeps,
): Promise<Result<DeleteMessageResult>> {
  const reservation = await deps.idempotency.tryReserve<DeleteMessageResult>({
    tenantId: command.tenantId,
    userId: command.actorUserId,
    scope: 'chat.message.delete',
    clientKey: command.clientKey,
    ttlSeconds: 60,
  });
  if (reservation.status === 'replay') return Result.ok(reservation.payload);

  const message = await deps.messages.findById(command.tenantId, command.messageId);
  if (!message) {
    await deps.idempotency.release({
      tenantId: command.tenantId,
      userId: command.actorUserId,
      scope: 'chat.message.delete',
      clientKey: command.clientKey,
      token: reservation.token,
    });
    return Result.fail(makeError('Chat.Message.NotFound', 'Message not found.'));
  }

  const now = new Date();
  const deleteResult = message.softDelete(command.actorUserId, command.actorCanModerate, now);
  if (!deleteResult.isSuccess) {
    await deps.idempotency.release({
      tenantId: command.tenantId,
      userId: command.actorUserId,
      scope: 'chat.message.delete',
      clientKey: command.clientKey,
      token: reservation.token,
    });
    return Result.fail(deleteResult.error);
  }
  await deps.messages.update(command.tenantId, message);

  const snapshot = message.toSnapshot();
  const deletedAtUtc = (snapshot.deletedAtUtc ?? now).toISOString();

  const event: MessageDeletedEvent = {
    eventId: randomUUID(),
    eventType: ChatEventTypes.MessageDeleted,
    tenantId: command.tenantId,
    correlationId: command.correlationId,
    occurredOnUtc: deletedAtUtc,
    conversationId: snapshot.conversationId,
    messageId: snapshot.id,
    deletedByUserId: command.actorUserId,
    deletedAtUtc,
  };
  await deps.publisher.enqueue(event);

  const result: DeleteMessageResult = {
    deleted: {
      messageId: snapshot.id,
      conversationId: snapshot.conversationId,
      deletedAtUtc,
    },
  };
  await deps.idempotency.commit({
    tenantId: command.tenantId,
    userId: command.actorUserId,
    scope: 'chat.message.delete',
    clientKey: command.clientKey,
    payload: result,
    token: reservation.token,
  });
  return Result.ok(result);
}
