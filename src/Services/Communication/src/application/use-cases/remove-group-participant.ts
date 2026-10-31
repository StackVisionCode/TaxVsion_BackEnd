import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../../domain/shared/result.js';
import type { IdempotencyStore } from '../ports/idempotency-store.js';
import type { IntegrationEventPublisher } from '../ports/integration-event-publisher.js';
import {
  ChatEventTypes,
  type ConversationParticipantRemovedEvent,
} from '../../contracts/events/chat-events.js';
import {
  actorHasGroupSuperpower,
  commitGroupChange,
  nameOf,
  removalSuccessionChange,
  type GroupActor,
  type GroupAdminDeps,
} from './group-administration.js';

/**
 * Cubre tanto "salir" (`targetUserId === actor`) como "expulsar" (distinto) — el dominio infiere `reason`.
 * F8b: en grupos el aggregate decide quién expulsa (Owner/Admin; un Admin no saca al Owner ni a otro Admin) y
 * resuelve la sucesión y el archivado sin staff (D-22). Salir nunca requiere permiso.
 */
export interface RemoveGroupParticipantCommand {
  readonly tenantId: string;
  readonly correlationId: string;
  readonly clientKey: string;
  readonly conversationId: string;
  readonly actor: GroupActor;
  readonly targetUserId: string;
}

export interface RemoveGroupParticipantResult {
  readonly conversationId: string;
  readonly removedParticipantUserId: string;
  readonly reason: 'Left' | 'Kicked';
  readonly newOwnerUserId: string | null;
  readonly archived: boolean;
}

export interface RemoveGroupParticipantDeps extends GroupAdminDeps {
  readonly idempotency: IdempotencyStore;
  readonly publisher: IntegrationEventPublisher;
}

export async function removeGroupParticipant(
  command: RemoveGroupParticipantCommand,
  deps: RemoveGroupParticipantDeps,
): Promise<Result<RemoveGroupParticipantResult>> {
  const scope = 'chat.conversation.remove_participant';
  const reservation = await deps.idempotency.tryReserve<RemoveGroupParticipantResult>({
    tenantId: command.tenantId,
    userId: command.actor.userId,
    scope,
    clientKey: command.clientKey,
    ttlSeconds: 60,
  });
  if (reservation.status === 'replay') {
    return Result.ok(reservation.payload);
  }
  const release = () =>
    deps.idempotency.release({
      tenantId: command.tenantId,
      userId: command.actor.userId,
      scope,
      clientKey: command.clientKey,
      token: reservation.token,
    });

  const conversation = await deps.conversations.findById(command.tenantId, command.conversationId, 0);
  if (!conversation) {
    await release();
    return Result.fail(makeError('Chat.Conversation.NotFound', 'Conversation not found.'));
  }

  const targetName = nameOf(conversation, command.targetUserId);
  const now = new Date();
  const removeResult = conversation.removeParticipant({
    actorUserId: command.actor.userId,
    targetUserId: command.targetUserId,
    actorIsTenantAdmin: actorHasGroupSuperpower(command.actor),
    now,
  });
  if (!removeResult.isSuccess) {
    await release();
    return Result.fail(removeResult.error);
  }
  const { reason, newOwnerUserId, archived } = removeResult.value;

  const event: ConversationParticipantRemovedEvent = {
    eventId: randomUUID(),
    eventType: ChatEventTypes.ConversationParticipantRemoved,
    tenantId: command.tenantId,
    correlationId: command.correlationId,
    occurredOnUtc: now.toISOString(),
    conversationId: conversation.id,
    removedByUserId: command.actor.userId,
    removedParticipantUserId: command.targetUserId,
    reason,
  };

  if (conversation.kind === 'Group') {
    const succession = removalSuccessionChange(conversation, { newOwnerUserId, archived });
    const text = reason === 'Left' ? `${targetName} left` : `${command.actor.displayName} removed ${targetName}`;
    await commitGroupChange(
      conversation,
      {
        systemText: [text, succession.extraText].filter(Boolean).join('. '),
        audit: [
          {
            action: reason === 'Left' ? 'MemberLeft' : 'MemberRemoved',
            actorUserId: command.actor.userId,
            targetUserId: command.targetUserId,
          },
          ...succession.audit,
        ],
        roleChanges: succession.roleChanges ?? [],
        updated: succession.updated ?? null,
        changedByUserId: null,
      },
      deps,
      now,
    );
  } else {
    await deps.conversations.save(conversation);
  }
  await deps.publisher.enqueue(event);

  const result: RemoveGroupParticipantResult = {
    conversationId: conversation.id,
    removedParticipantUserId: command.targetUserId,
    reason,
    newOwnerUserId,
    archived,
  };
  await deps.idempotency.commit({
    tenantId: command.tenantId,
    userId: command.actor.userId,
    scope,
    clientKey: command.clientKey,
    payload: result,
    token: reservation.token,
  });
  return Result.ok(result);
}
