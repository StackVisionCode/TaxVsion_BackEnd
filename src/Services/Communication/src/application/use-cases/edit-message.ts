import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../../domain/shared/result.js';
import type { MessageRepository } from '../ports/message-repository.js';
import type { IdempotencyStore } from '../ports/idempotency-store.js';
import type { IntegrationEventPublisher } from '../ports/integration-event-publisher.js';
import { ChatEventTypes, type MessageEditedEvent } from '../../contracts/events/chat-events.js';
import type { MessageEditedDto } from '../../contracts/socket/chat-socket-events.js';

export interface EditMessageCommand {
  readonly tenantId: string;
  readonly correlationId: string;
  readonly clientKey: string;
  readonly messageId: string;
  readonly senderUserId: string;
  readonly body: string;
}

export interface EditMessageResult {
  readonly edited: MessageEditedDto;
}

export interface EditMessageDeps {
  readonly messages: MessageRepository;
  readonly idempotency: IdempotencyStore;
  readonly publisher: IntegrationEventPublisher;
}

export async function editMessage(
  command: EditMessageCommand,
  deps: EditMessageDeps,
): Promise<Result<EditMessageResult>> {
  const reservation = await deps.idempotency.tryReserve<EditMessageResult>({
    tenantId: command.tenantId,
    userId: command.senderUserId,
    scope: 'chat.message.edit',
    clientKey: command.clientKey,
    ttlSeconds: 60,
  });
  if (reservation.status === 'replay') return Result.ok(reservation.payload);

  const message = await deps.messages.findById(command.tenantId, command.messageId);
  if (!message) {
    await deps.idempotency.release({
      tenantId: command.tenantId,
      userId: command.senderUserId,
      scope: 'chat.message.edit',
      clientKey: command.clientKey,
      token: reservation.token,
    });
    return Result.fail(makeError('Chat.Message.NotFound', 'Message not found.'));
  }

  const now = new Date();
  const editResult = message.editText(command.body, command.senderUserId, now);
  if (!editResult.isSuccess) {
    await deps.idempotency.release({
      tenantId: command.tenantId,
      userId: command.senderUserId,
      scope: 'chat.message.edit',
      clientKey: command.clientKey,
      token: reservation.token,
    });
    return Result.fail(editResult.error);
  }
  await deps.messages.update(command.tenantId, message);

  const snapshot = message.toSnapshot();
  const editedAtUtc = (snapshot.editedAtUtc ?? now).toISOString();

  const event: MessageEditedEvent = {
    eventId: randomUUID(),
    eventType: ChatEventTypes.MessageEdited,
    tenantId: command.tenantId,
    correlationId: command.correlationId,
    occurredOnUtc: editedAtUtc,
    conversationId: snapshot.conversationId,
    messageId: snapshot.id,
    editedByUserId: command.senderUserId,
    editedAtUtc,
  };
  await deps.publisher.enqueue(event);

  const dto: MessageEditedDto = {
    messageId: snapshot.id,
    conversationId: snapshot.conversationId,
    body: snapshot.body ?? '',
    editedAtUtc,
  };
  const result: EditMessageResult = { edited: dto };
  await deps.idempotency.commit({
    tenantId: command.tenantId,
    userId: command.senderUserId,
    scope: 'chat.message.edit',
    clientKey: command.clientKey,
    payload: result,
    token: reservation.token,
  });
  return Result.ok(result);
}
