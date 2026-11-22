import { randomUUID } from 'node:crypto';
import type { IntegrationEventPublisher } from '../ports/integration-event-publisher.js';
import type { SupportTicketRepository } from '../ports/support-ticket-repository.js';
import type { UserPermissionsProjectionRepository } from '../ports/user-permissions-projection-repository.js';
import { SupportEventTypes, type SupportMessageAddedEvent } from '../../contracts/events/support-events.js';
import { CommunicationPermissions } from '../../domain/shared/permissions.js';

export interface PublishSupportMessageAddedDeps {
  readonly publisher: IntegrationEventPublisher;
  readonly supportTickets?: Pick<SupportTicketRepository, 'findById' | 'findByConversationId'> | undefined;
  readonly userPermissions?: Pick<UserPermissionsProjectionRepository, 'findActiveSupportRecipients'> | undefined;
}

/**
 * Un mensaje en un ticket de soporte: Notification avisa a los agentes (oficina → soporte)
 * o a quien abrió (soporte → oficina). Sin texto ni asunto.
 */
export async function publishSupportMessageAdded(
  input: {
    readonly tenantId: string;
    readonly correlationId: string;
    readonly ticketId: string;
    readonly conversationId: string;
    readonly messageId: string;
    readonly senderUserId: string;
    readonly sentAtUtc: string;
  },
  deps: PublishSupportMessageAddedDeps,
): Promise<void> {
  if (!deps.supportTickets) return;
  // Tickets viejos: su conversación guarda un id que no es el del ticket; se busca por la conversación.
  const ticket =
    (await deps.supportTickets.findById(input.ticketId)) ??
    (await deps.supportTickets.findByConversationId?.(input.conversationId)) ??
    null;
  if (!ticket) return;
  // El agente escribe como el marcador del tenant de soporte.
  const senderRole = input.senderUserId === ticket.agentTenantId ? 'Agent' : 'Customer';
  const recipients =
    senderRole === 'Customer' && ticket.assignedAgentId === null && deps.userPermissions
      ? await deps.userPermissions.findActiveSupportRecipients(ticket.agentTenantId, CommunicationPermissions.SupportAgent)
      : [];
  const event: SupportMessageAddedEvent = {
    eventId: randomUUID(),
    eventType: SupportEventTypes.MessageAdded,
    tenantId: input.tenantId,
    correlationId: input.correlationId,
    occurredOnUtc: input.sentAtUtc,
    ticketId: ticket.id,
    messageId: input.messageId,
    senderUserId: input.senderUserId,
    senderRole,
    sentAtUtc: input.sentAtUtc,
    conversationId: ticket.conversationId,
    agentTenantId: ticket.agentTenantId,
    openedByUserId: ticket.openedByUserId,
    assignedAgentId: ticket.assignedAgentId,
    supportRecipientUserIds: recipients.map((r) => r.userId),
  };
  await deps.publisher.enqueue(event);
}
