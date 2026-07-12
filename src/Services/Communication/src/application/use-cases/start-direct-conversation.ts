import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../../domain/shared/result.js';
import { Conversation } from '../../domain/conversations/conversation.js';
import { computeDirectUniquenessKey } from '../../domain/conversations/uniqueness-key.js';
import type { ConversationRepository } from '../ports/conversation-repository.js';
import type { IdempotencyStore } from '../ports/idempotency-store.js';
import type { IntegrationEventPublisher } from '../ports/integration-event-publisher.js';
import type { TenantSettingsProvider } from '../ports/tenant-settings-provider.js';
import { ChatEventTypes, type ConversationStartedEvent } from '../../contracts/events/chat-events.js';

/**
 * Comando: iniciar (o encontrar) un chat directo entre iniciador y destinatario
 * dentro del mismo tenant. Es idempotente por (tenantId, initiator, clientKey)
 * — si el cliente reintenta, recibe el mismo aggregate id.
 *
 * Reglas de negocio:
 *   1. Chat habilitado en el tenant.
 *   2. Si ambos son Employee, requiere el flag employeeToEmployeeChatEnabled.
 *   3. La conversation es unique por (tenantId, direct:userA:userB ordenado).
 *   4. El display name del destinatario se resuelve por el caller (viene del
 *      read-model de usuarios); el use case NO consulta Auth.
 */

export interface StartDirectConversationCommand {
  readonly tenantId: string;
  readonly correlationId: string;
  readonly clientKey: string;
  readonly initiator: { userId: string; displayName: string; actorType: string };
  readonly recipient: {
    userId: string;
    displayName: string;
    actorType: string;
    isPrimaryPreparer?: boolean;
  };
}

export interface StartDirectConversationResult {
  readonly conversationId: string;
  readonly wasCreated: boolean;
}

export interface StartDirectConversationDeps {
  readonly conversations: ConversationRepository;
  readonly idempotency: IdempotencyStore;
  readonly publisher: IntegrationEventPublisher;
  readonly settings: TenantSettingsProvider;
}

export async function startDirectConversation(
  command: StartDirectConversationCommand,
  deps: StartDirectConversationDeps,
): Promise<Result<StartDirectConversationResult>> {
  const settings = await deps.settings.get(command.tenantId);
  if (!settings.chatEnabled) {
    return Result.fail(makeError('Chat.Disabled', 'Chat is disabled for this tenant.'));
  }
  const bothEmployees =
    command.initiator.actorType === 'TenantEmployee' && command.recipient.actorType === 'TenantEmployee';
  if (bothEmployees && !settings.employeeToEmployeeChatEnabled) {
    return Result.fail(
      makeError(
        'Chat.EmployeeToEmployeeDisabled',
        'Employee-to-employee chat is disabled for this tenant.',
      ),
    );
  }

  const reservation = await deps.idempotency.tryReserve<StartDirectConversationResult>({
    tenantId: command.tenantId,
    userId: command.initiator.userId,
    scope: 'chat.conversation.start_direct',
    clientKey: command.clientKey,
    ttlSeconds: 60,
  });
  if (reservation.status === 'replay') {
    return Result.ok(reservation.payload);
  }

  const uniquenessKey = computeDirectUniquenessKey(
    command.initiator.userId,
    command.recipient.userId,
  );
  const existing = await deps.conversations.findByUniquenessKey(command.tenantId, uniquenessKey);
  if (existing) {
    const result: StartDirectConversationResult = { conversationId: existing.id, wasCreated: false };
    await deps.idempotency.commit({
      tenantId: command.tenantId,
      userId: command.initiator.userId,
      scope: 'chat.conversation.start_direct',
      clientKey: command.clientKey,
      payload: result,
      token: reservation.token,
    });
    return Result.ok(result);
  }

  const conversationResult = Conversation.startDirect({
    tenantId: command.tenantId,
    initiator: command.initiator,
    recipient: command.recipient,
  });
  if (!conversationResult.isSuccess) {
    await deps.idempotency.release({
      tenantId: command.tenantId,
      userId: command.initiator.userId,
      scope: 'chat.conversation.start_direct',
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
    kind: 'Direct',
    createdByUserId: command.initiator.userId,
    participantUserIds: [command.initiator.userId, command.recipient.userId],
  };

  await deps.conversations.save(conversation);
  await deps.publisher.enqueue(startedEvent);

  const result: StartDirectConversationResult = { conversationId: conversation.id, wasCreated: true };
  await deps.idempotency.commit({
    tenantId: command.tenantId,
    userId: command.initiator.userId,
    scope: 'chat.conversation.start_direct',
    clientKey: command.clientKey,
    payload: result,
    token: reservation.token,
  });
  return Result.ok(result);
}
