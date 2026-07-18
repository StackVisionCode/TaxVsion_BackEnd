import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../../domain/shared/result.js';
import { Conversation } from '../../domain/conversations/conversation.js';
import type { ConversationRepository } from '../ports/conversation-repository.js';
import type { IdempotencyStore } from '../ports/idempotency-store.js';
import type { IntegrationEventPublisher } from '../ports/integration-event-publisher.js';
import type { TenantSettingsProvider } from '../ports/tenant-settings-provider.js';
import { ChatEventTypes, type ConversationStartedEvent } from '../../contracts/events/chat-events.js';

/**
 * Comando: crear un grupo. `groupId` NO lo manda el cliente — lo genera este
 * use case (es la clave de unicidad interna, ver uniqueness-key.ts) — cada
 * llamada exitosa siempre crea un grupo nuevo, no hay dedupe por contenido
 * como en Direct.
 */
export interface StartGroupConversationCommand {
  readonly tenantId: string;
  readonly correlationId: string;
  readonly clientKey: string;
  readonly creator: { userId: string; displayName: string; actorType: string };
  readonly title: string;
  readonly members: ReadonlyArray<{ userId: string; displayName: string; actorType: string }>;
}

export interface StartGroupConversationResult {
  readonly conversationId: string;
}

export interface StartGroupConversationDeps {
  readonly conversations: ConversationRepository;
  readonly idempotency: IdempotencyStore;
  readonly publisher: IntegrationEventPublisher;
  readonly settings: TenantSettingsProvider;
}

export async function startGroupConversation(
  command: StartGroupConversationCommand,
  deps: StartGroupConversationDeps,
): Promise<Result<StartGroupConversationResult>> {
  const settings = await deps.settings.get(command.tenantId);
  if (!settings.chatEnabled) {
    return Result.fail(makeError('Chat.Disabled', 'Chat is disabled for this tenant.'));
  }
  if (!settings.internalGroupsEnabled) {
    return Result.fail(makeError('Chat.GroupsDisabled', 'Internal groups are disabled for this tenant.'));
  }

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

  const conversationResult = Conversation.startGroup({
    tenantId: command.tenantId,
    groupId: randomUUID(),
    title: command.title,
    creator: command.creator,
    members: command.members,
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

  const startedEvent: ConversationStartedEvent = {
    eventId: randomUUID(),
    eventType: ChatEventTypes.ConversationStarted,
    tenantId: command.tenantId,
    correlationId: command.correlationId,
    occurredOnUtc: new Date().toISOString(),
    conversationId: conversation.id,
    kind: 'Group',
    createdByUserId: command.creator.userId,
    participantUserIds: [command.creator.userId, ...command.members.map((m) => m.userId)],
  };

  await deps.conversations.save(conversation);
  await deps.publisher.enqueue(startedEvent);

  const result: StartGroupConversationResult = { conversationId: conversation.id };
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
