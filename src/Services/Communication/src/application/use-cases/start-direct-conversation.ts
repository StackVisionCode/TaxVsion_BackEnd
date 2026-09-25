import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../../domain/shared/result.js';
import { Conversation } from '../../domain/conversations/conversation.js';
import { computeDirectUniquenessKey } from '../../domain/conversations/uniqueness-key.js';
import type { ConversationRepository } from '../ports/conversation-repository.js';
import type { IdempotencyStore } from '../ports/idempotency-store.js';
import type { IntegrationEventPublisher } from '../ports/integration-event-publisher.js';
import type { TenantSettingsProvider } from '../ports/tenant-settings-provider.js';
import type { CustomerPortalAccountRepository } from '../ports/customer-portal-account-repository.js';
import type { CustomerPreparerAssignmentRepository } from '../ports/customer-preparer-assignment-repository.js';
import type { CustomerAssignmentProjectionRepository } from '../ports/customer-assignment-projection-repository.js';
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
 *   5. Fase B5 / P2.5 — si el tenant tiene restrictCustomerChatToAssignedPreparer,
 *      un chat que involucra a un customer solo se permite si el lado staff esta
 *      asignado a ese customer en la proyeccion M:N (CustomerAssignmentProjection)
 *      — CUALQUIER staff asignado, no solo el primary. isPrimaryPreparer se sigue
 *      resolviendo aparte (1:1) solo para marcar al participante.
 */

export interface StartDirectConversationCommand {
  readonly tenantId: string;
  readonly correlationId: string;
  readonly clientKey: string;
  readonly initiator: { userId: string; displayName: string; actorType: string };
  readonly recipient: { userId: string; displayName: string; actorType: string };
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
  readonly customerPortalAccounts: CustomerPortalAccountRepository;
  readonly customerPreparerAssignments: CustomerPreparerAssignmentRepository;
  readonly customerAssignments: CustomerAssignmentProjectionRepository;
  // Flag GLOBAL de visibilidad por asignacion (P2): con ON, el gate cliente<->staff
  // aplica en TODOS los tenants aunque el tenant no tenga restrictCustomerChatToAssignedPreparer.
  // Ausente = OFF (default seguro); el container siempre lo provee.
  readonly assignmentVisibilityEnabled?: boolean;
}

/**
 * Fase B4 (chat tipado) — el destinatario es "el preparador primario" del
 * cliente si: (a) alguno de los dos lados de la conversation es un actor
 * 'CustomerPortal', y (b) ese customer tiene un preparador asignado
 * (CustomerPreparerAssignment) cuyo UserId es justo el del destinatario.
 * Si ninguno de los dos es customer, o el customer no tiene preparador
 * asignado, o el destinatario no es ese preparador -> false. Reemplaza el
 * campo que antes solo se declaraba pero nunca se poblaba (isPrimaryPreparer
 * llegaba siempre undefined desde chat-handlers.ts).
 */
async function resolveIsPrimaryPreparer(
  command: StartDirectConversationCommand,
  deps: Pick<StartDirectConversationDeps, 'customerPortalAccounts' | 'customerPreparerAssignments'>,
): Promise<boolean> {
  const customerSide =
    command.initiator.actorType === 'CustomerPortal'
      ? command.initiator
      : command.recipient.actorType === 'CustomerPortal'
        ? command.recipient
        : null;
  if (!customerSide) return false;

  const portalAccount = await deps.customerPortalAccounts.findActiveByUserId(customerSide.userId);
  if (!portalAccount) return false;

  const assignment = await deps.customerPreparerAssignments.findByCustomerId(
    command.tenantId,
    portalAccount.customerId,
  );
  return assignment?.preparerUserId === command.recipient.userId;
}

/**
 * Gate de chat cliente↔staff (P2.5, modelo M:N). Un chat que involucra a un
 * customer solo se permite si el lado STAFF (el que NO es CustomerPortal) esta
 * asignado a ese customer en la proyeccion M:N — CUALQUIER asignado, no solo el
 * primary (el primary tambien esta en el set, asi que subsume el viejo check
 * 1:1). Devuelve { involvesCustomer, staffIsAssigned }: si no involucra a un
 * customer, el gate no aplica; si lo involucra pero el customer no tiene cuenta
 * de portal activa o el staff no esta asignado -> staffIsAssigned=false.
 */
async function resolveCustomerChatAccess(
  command: StartDirectConversationCommand,
  deps: Pick<StartDirectConversationDeps, 'customerPortalAccounts' | 'customerAssignments'>,
): Promise<{ involvesCustomer: boolean; staffIsAssigned: boolean }> {
  const initiatorIsCustomer = command.initiator.actorType === 'CustomerPortal';
  const recipientIsCustomer = command.recipient.actorType === 'CustomerPortal';
  if (!initiatorIsCustomer && !recipientIsCustomer) {
    return { involvesCustomer: false, staffIsAssigned: false };
  }
  const customerSide = initiatorIsCustomer ? command.initiator : command.recipient;
  const staffSide = initiatorIsCustomer ? command.recipient : command.initiator;

  const portalAccount = await deps.customerPortalAccounts.findActiveByUserId(customerSide.userId);
  if (!portalAccount) return { involvesCustomer: true, staffIsAssigned: false };

  const staffIsAssigned = await deps.customerAssignments.isAssigned(
    command.tenantId,
    portalAccount.customerId,
    staffSide.userId,
  );
  return { involvesCustomer: true, staffIsAssigned };
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

  const isPrimaryPreparer = await resolveIsPrimaryPreparer(command, deps);
  if (deps.assignmentVisibilityEnabled || settings.restrictCustomerChatToAssignedPreparer) {
    const { involvesCustomer, staffIsAssigned } = await resolveCustomerChatAccess(command, deps);
    if (involvesCustomer && !staffIsAssigned) {
      return Result.fail(
        makeError(
          'Chat.NotAssignedPreparer',
          'This tenant only allows customers to chat with staff assigned to them.',
        ),
      );
    }
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
    recipient: { ...command.recipient, isPrimaryPreparer },
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
