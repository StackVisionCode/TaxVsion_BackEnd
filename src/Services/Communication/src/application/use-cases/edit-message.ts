import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../../domain/shared/result.js';
import type { MessageRepository } from '../ports/message-repository.js';
import type { ConversationRepository } from '../ports/conversation-repository.js';
import type { EffectiveSettingsResolver } from '../ports/effective-settings-resolver.js';
import type { MessageEditPolicy } from '../../domain/conversations/message.js';
import type { ConversationKind } from '../../domain/conversations/conversation-kind.js';
import type { ChatEditPolicy } from '../../domain/settings/effective-communication-settings.js';
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
  readonly conversations: Pick<ConversationRepository, 'findById'>;
  readonly effectiveSettings: Pick<EffectiveSettingsResolver, 'resolve'>;
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
  // F8: la ventana depende del tipo de chat y quien edita tiene que poder escribir hoy (no archivada, sigue dentro).
  const conversation = await deps.conversations.findById(command.tenantId, message.toSnapshot().conversationId, 0);
  const canWrite = conversation
    ? conversation.ensureCanWrite(command.senderUserId)
    : Result.fail(makeError('Chat.Conversation.NotFound', 'Conversation not found.'));
  const policy = conversation
    ? editPolicyFor((await deps.effectiveSettings.resolve(command.tenantId)).chat.edit, conversation.kind)
    : null;
  const editResult = !canWrite.isSuccess
    ? Result.fail(canWrite.error)
    : message.editText(command.body, command.senderUserId, policy as MessageEditPolicy, now);
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
  await deps.messages.recordEdit(command.tenantId, message, {
    revision: editResult.value.revision,
    previousBody: editResult.value.previousBody,
    editedByUserId: command.senderUserId,
    editedAtUtc: now,
  });

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
    editCount: snapshot.editCount ?? 0,
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

/** F8: ventana y tope del tipo de chat de la conversación. */
export function editPolicyFor(policy: ChatEditPolicy, kind: ConversationKind): MessageEditPolicy {
  return { windowMinutes: policy.windowMinutes[kind], maxEdits: policy.maxEdits };
}
