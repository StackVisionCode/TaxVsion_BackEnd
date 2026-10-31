import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../../domain/shared/result.js';
import { Conversation } from '../../domain/conversations/conversation.js';
import type { ConversationRepository } from '../ports/conversation-repository.js';
import type { IdempotencyStore } from '../ports/idempotency-store.js';
import type { IntegrationEventPublisher } from '../ports/integration-event-publisher.js';
import type { TenantSettingsProvider } from '../ports/tenant-settings-provider.js';
import type { EffectiveSettingsResolver } from '../ports/effective-settings-resolver.js';
import type { ConversationAuditRepository } from '../ports/conversation-audit-repository.js';
import { ChatEventTypes, type ConversationStartedEvent } from '../../contracts/events/chat-events.js';
import { admitGroupMembers, type GroupMembershipPolicyDeps } from './group-membership-policy.js';
import { logger } from '../../infrastructure/logger/logger.js';

/**
 * Comando: crear un grupo. `groupId` NO lo manda el cliente — lo genera este
 * use case (es la clave de unicidad interna, ver uniqueness-key.ts) — cada
 * llamada exitosa siempre crea un grupo nuevo, no hay dedupe por contenido
 * como en Direct. F8b: los miembros pasan por la política única (T-18) y el
 * mínimo es 1 además del creador (ADD-03 H-7: un grupo de dos es válido).
 */
export interface StartGroupConversationCommand {
  readonly tenantId: string;
  readonly correlationId: string;
  readonly clientKey: string;
  readonly creator: { userId: string; displayName: string; actorType: string };
  readonly title: string;
  readonly description?: string | null;
  readonly memberUserIds: readonly string[];
}

export interface StartGroupConversationResult {
  readonly conversationId: string;
  /** Miembros admitidos (sin el creador), para avisarles y unir sus sockets. */
  readonly memberUserIds: readonly string[];
}

export interface StartGroupConversationDeps extends GroupMembershipPolicyDeps {
  readonly conversations: ConversationRepository;
  readonly idempotency: IdempotencyStore;
  readonly publisher: IntegrationEventPublisher;
  readonly settings: TenantSettingsProvider;
  readonly effectiveSettings: Pick<EffectiveSettingsResolver, 'resolve'>;
  readonly conversationAudit?: ConversationAuditRepository | undefined;
}

export async function startGroupConversation(
  command: StartGroupConversationCommand,
  deps: StartGroupConversationDeps,
): Promise<Result<StartGroupConversationResult>> {
  const settings = await deps.settings.get(command.tenantId);
  if (!settings.chatEnabled) {
    return Result.fail(makeError('Chat.Disabled', 'Chat is disabled for this tenant.'));
  }
  // D-16: los grupos internos son un interruptor global de la plataforma, no de la oficina.
  const effective = await deps.effectiveSettings.resolve(command.tenantId);
  if (!effective.chat.internalGroupsEnabled) {
    return Result.fail(makeError('Chat.GroupsDisabled', 'Internal groups are disabled on this platform.'));
  }

  const memberUserIds = [...new Set(command.memberUserIds)].filter((id) => id !== command.creator.userId);
  if (memberUserIds.length === 0) {
    return Result.fail(makeError('Chat.Conversation.NoMembers', 'Group requires at least one member.'));
  }
  const admitted = await admitGroupMembers(
    {
      tenantId: command.tenantId,
      actor: command.creator,
      candidates: memberUserIds.map((userId) => ({ userId })),
      existingClientCount: 0,
      settings,
    },
    deps,
  );
  if (!admitted.isSuccess) return Result.fail(admitted.error);

  const reservation = await deps.idempotency.tryReserve<StartGroupConversationResult>({
    tenantId: command.tenantId,
    userId: command.creator.userId,
    scope: 'chat.conversation.start_group',
    clientKey: command.clientKey,
    ttlSeconds: 60,
  });
  if (reservation.status === 'replay') {
    return Result.ok(reservation.payload);
  }

  const now = new Date();
  const conversationResult = Conversation.startGroup({
    tenantId: command.tenantId,
    groupId: randomUUID(),
    title: command.title,
    description: command.description ?? null,
    creator: command.creator,
    members: admitted.value,
    now,
  });
  if (!conversationResult.isSuccess) {
    await deps.idempotency.release({
      tenantId: command.tenantId,
      userId: command.creator.userId,
      scope: 'chat.conversation.start_group',
      clientKey: command.clientKey,
      token: reservation.token,
    });
    return Result.fail(conversationResult.error);
  }
  const conversation = conversationResult.value;
  conversation.appendSystemMessage({ body: `${command.creator.displayName} created the group`, now });

  const startedEvent: ConversationStartedEvent = {
    eventId: randomUUID(),
    eventType: ChatEventTypes.ConversationStarted,
    tenantId: command.tenantId,
    correlationId: command.correlationId,
    occurredOnUtc: now.toISOString(),
    conversationId: conversation.id,
    kind: 'Group',
    createdByUserId: command.creator.userId,
    participantUserIds: [command.creator.userId, ...admitted.value.map((m) => m.userId)],
  };

  await deps.conversations.save(conversation);
  await deps.publisher.enqueue(startedEvent);
  await deps.conversationAudit
    ?.append([
      {
        tenantId: command.tenantId,
        conversationId: conversation.id,
        action: 'GroupCreated',
        actorUserId: command.creator.userId,
        targetUserId: null,
        metadata: { memberUserIds: admitted.value.map((m) => m.userId) },
        occurredAtUtc: now,
      },
    ])
    .catch((err: unknown) => logger.error({ err, conversationId: conversation.id }, 'group audit: no se pudo registrar'));

  const result: StartGroupConversationResult = {
    conversationId: conversation.id,
    memberUserIds: admitted.value.map((m) => m.userId),
  };
  await deps.idempotency.commit({
    tenantId: command.tenantId,
    userId: command.creator.userId,
    scope: 'chat.conversation.start_group',
    clientKey: command.clientKey,
    payload: result,
    token: reservation.token,
  });
  return Result.ok(result);
}
