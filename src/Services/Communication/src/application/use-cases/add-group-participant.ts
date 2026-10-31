import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../../domain/shared/result.js';
import { ConversationKind } from '../../domain/conversations/conversation-kind.js';
import type { IdempotencyStore } from '../ports/idempotency-store.js';
import type { IntegrationEventPublisher } from '../ports/integration-event-publisher.js';
import type { TenantSettingsProvider } from '../ports/tenant-settings-provider.js';
import type { NotificationRepository } from '../ports/notification-repository.js';
import {
  ChatEventTypes,
  type ConversationParticipantAddedEvent,
} from '../../contracts/events/chat-events.js';
import { admitGroupMembers, groupComposition, type GroupMembershipPolicyDeps } from './group-membership-policy.js';
import { actorHasGroupSuperpower, commitGroupChange, notifyAddedToGroup, type GroupActor, type GroupAdminDeps } from './group-administration.js';

/**
 * F8b: alta directa a un grupo (D-21: sin invitación pendiente; mensaje System y aviso en la campana).
 * Quién puede agregar lo decide el aggregate (Owner/Admin, o el superpoder del Tenant Admin); a quién, la
 * política única de membresía (T-18). Reingresar a alguien reactiva su fila y su historial arranca de nuevo.
 */
export interface AddGroupParticipantCommand {
  readonly tenantId: string;
  readonly correlationId: string;
  readonly clientKey: string;
  readonly conversationId: string;
  readonly actor: GroupActor;
  readonly newMemberUserId: string;
}

export interface AddGroupParticipantResult {
  readonly conversationId: string;
  readonly newParticipantUserId: string;
  readonly newParticipantDisplayName: string;
}

export interface AddGroupParticipantDeps extends GroupMembershipPolicyDeps, GroupAdminDeps {
  readonly idempotency: IdempotencyStore;
  readonly publisher: IntegrationEventPublisher;
  readonly settings: TenantSettingsProvider;
  readonly notifications?: NotificationRepository | undefined;
}

export async function addGroupParticipant(
  command: AddGroupParticipantCommand,
  deps: AddGroupParticipantDeps,
): Promise<Result<AddGroupParticipantResult>> {
  const scope = 'chat.conversation.add_participant';
  const reservation = await deps.idempotency.tryReserve<AddGroupParticipantResult>({
    tenantId: command.tenantId,
    userId: command.actor.userId,
    scope,
    clientKey: command.clientKey,
    ttlSeconds: 60,
  });
  if (reservation.status === 'replay') {
    return Result.ok(reservation.payload);
  }
  const fail = async <T>(error: { code: string; message: string }): Promise<Result<T>> => {
    await deps.idempotency.release({
      tenantId: command.tenantId,
      userId: command.actor.userId,
      scope,
      clientKey: command.clientKey,
      token: reservation.token,
    });
    return Result.fail(error);
  };

  const conversation = await deps.conversations.findById(command.tenantId, command.conversationId, 0);
  if (!conversation) return fail(makeError('Chat.Conversation.NotFound', 'Conversation not found.'));
  if (conversation.kind !== ConversationKind.Group) {
    return fail(makeError('Chat.Conversation.NotGroup', 'Only groups support adding members.'));
  }

  const settings = await deps.settings.get(command.tenantId);
  const composition = groupComposition(conversation.getParticipantSnapshots());
  const admitted = await admitGroupMembers(
    {
      tenantId: command.tenantId,
      actor: command.actor,
      candidates: [{ userId: command.newMemberUserId }],
      existingClientCount: composition.clientCount,
      existingHasNonEmployee: composition.hasNonEmployee,
      settings,
    },
    deps,
  );
  if (!admitted.isSuccess) return fail(admitted.error);
  const member = admitted.value[0];
  if (!member) return fail(makeError('Directory.Unresolved', 'Member could not be resolved yet.'));

  const now = new Date();
  const addResult = conversation.addParticipant({
    actorUserId: command.actor.userId,
    newMember: member,
    actorIsTenantAdmin: actorHasGroupSuperpower(command.actor),
    now,
  });
  if (!addResult.isSuccess) return fail(addResult.error);

  const event: ConversationParticipantAddedEvent = {
    eventId: randomUUID(),
    eventType: ChatEventTypes.ConversationParticipantAdded,
    tenantId: command.tenantId,
    correlationId: command.correlationId,
    occurredOnUtc: now.toISOString(),
    conversationId: conversation.id,
    addedByUserId: command.actor.userId,
    newParticipantUserId: member.userId,
  };

  await commitGroupChange(
    conversation,
    {
      systemText: `${command.actor.displayName} added ${member.displayName}`,
      audit: [{ action: 'MemberAdded', actorUserId: command.actor.userId, targetUserId: member.userId, metadata: { actorType: member.actorType } }],
    },
    deps,
    now,
  );
  await deps.publisher.enqueue(event);
  await notifyAddedToGroup(
    {
      tenantId: command.tenantId,
      sourceEventId: event.eventId,
      correlationId: command.correlationId,
      conversationId: conversation.id,
      groupTitle: conversation.title,
      addedUserId: member.userId,
      actor: command.actor,
    },
    deps,
  );

  const result: AddGroupParticipantResult = {
    conversationId: conversation.id,
    newParticipantUserId: member.userId,
    newParticipantDisplayName: member.displayName,
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
