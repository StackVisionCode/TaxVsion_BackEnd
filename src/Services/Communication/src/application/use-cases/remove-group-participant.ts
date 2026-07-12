import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../../domain/shared/result.js';
import type { ConversationRepository } from '../ports/conversation-repository.js';
import type { IdempotencyStore } from '../ports/idempotency-store.js';
import type { IntegrationEventPublisher } from '../ports/integration-event-publisher.js';
import {
  ChatEventTypes,
  type ConversationParticipantRemovedEvent,
} from '../../contracts/events/chat-events.js';

/**
 * Cubre tanto "salir" (`targetUserId === actorUserId`) como "expulsar"
 * (distinto) — el dominio infiere `reason` de eso. El handler decide si el
 * actor tiene permiso para expulsar a otro (`communication.group.
 * manage_members`); salir de un grupo del que ya se es participante nunca
 * requiere ese permiso.
 */
export interface RemoveGroupParticipantCommand {
  readonly tenantId: string;
  readonly correlationId: string;
  readonly clientKey: string;
  readonly conversationId: string;
  readonly actorUserId: string;
  readonly targetUserId: string;
}

export interface RemoveGroupParticipantResult {
  readonly conversationId: string;
  readonly removedParticipantUserId: string;
  readonly reason: 'Left' | 'Kicked';
}

export interface RemoveGroupParticipantDeps {
  readonly conversations: ConversationRepository;
  readonly idempotency: IdempotencyStore;
  readonly publisher: IntegrationEventPublisher;
}

export async function removeGroupParticipant(
  command: RemoveGroupParticipantCommand,
  deps: RemoveGroupParticipantDeps,
): Promise<Result<RemoveGroupParticipantResult>> {
  const reservation = await deps.idempotency.tryReserve<RemoveGroupParticipantResult>({
    tenantId: command.tenantId,
    userId: command.actorUserId,
    scope: 'chat.conversation.remove_participant',
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
      scope: 'chat.conversation.remove_participant',
      clientKey: command.clientKey,
      token: reservation.token,
    });

  const conversation = await deps.conversations.findById(command.tenantId, command.conversationId);
  if (!conversation) {
    await release();
    return Result.fail(makeError('Chat.Conversation.NotFound', 'Conversation not found.'));
  }

  const removeResult = conversation.removeParticipant({
    actorUserId: command.actorUserId,
    targetUserId: command.targetUserId,
  });
  if (!removeResult.isSuccess) {
    await release();
    return Result.fail(removeResult.error);
  }

  const event: ConversationParticipantRemovedEvent = {
    eventId: randomUUID(),
    eventType: ChatEventTypes.ConversationParticipantRemoved,
    tenantId: command.tenantId,
    correlationId: command.correlationId,
    occurredOnUtc: new Date().toISOString(),
    conversationId: conversation.id,
    removedByUserId: command.actorUserId,
    removedParticipantUserId: command.targetUserId,
    reason: removeResult.value.reason,
  };

  await deps.conversations.save(conversation);
  await deps.publisher.enqueue(event);

  const result: RemoveGroupParticipantResult = {
    conversationId: conversation.id,
    removedParticipantUserId: command.targetUserId,
    reason: removeResult.value.reason,
  };
  await deps.idempotency.commit({
    tenantId: command.tenantId,
    userId: command.actorUserId,
    scope: 'chat.conversation.remove_participant',
    clientKey: command.clientKey,
    payload: result,
    token: reservation.token,
  });
  return Result.ok(result);
}
