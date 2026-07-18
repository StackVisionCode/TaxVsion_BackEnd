import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../../domain/shared/result.js';
import type { ConversationRepository } from '../ports/conversation-repository.js';
import type { IdempotencyStore } from '../ports/idempotency-store.js';
import type { IntegrationEventPublisher } from '../ports/integration-event-publisher.js';
import {
  ChatEventTypes,
  type ConversationParticipantAddedEvent,
} from '../../contracts/events/chat-events.js';

export interface AddGroupParticipantCommand {
  readonly tenantId: string;
  readonly correlationId: string;
  readonly clientKey: string;
  readonly conversationId: string;
  readonly actorUserId: string;
  readonly newMember: { userId: string; displayName: string; actorType: string };
}

export interface AddGroupParticipantResult {
  readonly conversationId: string;
  readonly newParticipantUserId: string;
}

export interface AddGroupParticipantDeps {
  readonly conversations: ConversationRepository;
  readonly idempotency: IdempotencyStore;
  readonly publisher: IntegrationEventPublisher;
}

export async function addGroupParticipant(
  command: AddGroupParticipantCommand,
  deps: AddGroupParticipantDeps,
): Promise<Result<AddGroupParticipantResult>> {
  const reservation = await deps.idempotency.tryReserve<AddGroupParticipantResult>({
    tenantId: command.tenantId,
    userId: command.actorUserId,
    scope: 'chat.conversation.add_participant',
    clientKey: command.clientKey,
    ttlSeconds: 60,
  });
  if (reservation.status === 'replay') {
    return Result.ok(reservation.payload);
  }

  const release = () =>
    deps.idempotency.release({
      tenantId: command.tenantId,
      userId: command.actorUserId,
      scope: 'chat.conversation.add_participant',
      clientKey: command.clientKey,
      token: reservation.token,
    });

  const conversation = await deps.conversations.findById(command.tenantId, command.conversationId);
  if (!conversation) {
    await release();
    return Result.fail(makeError('Chat.Conversation.NotFound', 'Conversation not found.'));
  }

  const addResult = conversation.addParticipant({
    actorUserId: command.actorUserId,
    newMember: command.newMember,
  });
  if (!addResult.isSuccess) {
    await release();
    return Result.fail(addResult.error);
  }

  const event: ConversationParticipantAddedEvent = {
    eventId: randomUUID(),
    eventType: ChatEventTypes.ConversationParticipantAdded,
    tenantId: command.tenantId,
    correlationId: command.correlationId,
    occurredOnUtc: new Date().toISOString(),
    conversationId: conversation.id,
    addedByUserId: command.actorUserId,
    newParticipantUserId: command.newMember.userId,
  };

  await deps.conversations.save(conversation);
  await deps.publisher.enqueue(event);

  const result: AddGroupParticipantResult = {
    conversationId: conversation.id,
    newParticipantUserId: command.newMember.userId,
  };
  await deps.idempotency.commit({
    tenantId: command.tenantId,
    userId: command.actorUserId,
    scope: 'chat.conversation.add_participant',
    clientKey: command.clientKey,
    payload: result,
    token: reservation.token,
  });
  return Result.ok(result);
}
