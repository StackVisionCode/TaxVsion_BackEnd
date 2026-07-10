import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../../domain/shared/result.js';
import { SupportTicket } from '../../domain/support/support-ticket.js';
import { Conversation } from '../../domain/conversations/conversation.js';
import type { SupportCategory, SupportPriority } from '../../domain/support/support-enums.js';
import type { ConversationRepository } from '../ports/conversation-repository.js';
import type { SupportTicketRepository } from '../ports/support-ticket-repository.js';
import type { IntegrationEventPublisher } from '../ports/integration-event-publisher.js';
import type { PlatformTenantProvider } from '../ports/platform-tenant-provider.js';
import type { TenantSettingsProvider } from '../ports/tenant-settings-provider.js';
import { SupportEventTypes, type SupportOpenedEvent } from '../../contracts/events/support-events.js';

/**
 * Abre un ticket de soporte desde el tenant customer hacia el PlatformTenant.
 * Solo la Conversation vive en el tenant customer (mensajes filtran ahi); el
 * SupportTicket es cross-tenant (customer tenant + agent tenant).
 */
export interface OpenSupportTicketCommand {
  readonly tenantId: string;
  readonly correlationId: string;
  readonly opener: { userId: string; displayName: string; actorType: string };
  readonly subject: string;
  readonly category: SupportCategory;
  readonly priority?: SupportPriority;
  readonly initialMessage?: string;
}

export interface OpenSupportTicketResult {
  readonly ticketId: string;
  readonly conversationId: string;
}

export interface OpenSupportTicketDeps {
  readonly conversations: ConversationRepository;
  readonly supportTickets: SupportTicketRepository;
  readonly publisher: IntegrationEventPublisher;
  readonly platform: PlatformTenantProvider;
  readonly settings: TenantSettingsProvider;
}

export async function openSupportTicket(
  cmd: OpenSupportTicketCommand,
  deps: OpenSupportTicketDeps,
): Promise<Result<OpenSupportTicketResult>> {
  const settings = await deps.settings.get(cmd.tenantId);
  if (!settings.chatEnabled) {
    return Result.fail(makeError('Support.Disabled', 'Communication is disabled for this tenant.'));
  }

  const agentTenantId = deps.platform.getPlatformTenantId();

  // Placeholder para el "agente": la Conversation Support siempre nace con dos
  // participantes — opener + un `unassigned` marker que se reemplaza al Claim.
  // Como el agente real se conoce en `claim`, dejamos al opener como Owner y
  // otro participante marker con el agentTenantId al pop del claim (via
  // `Conversation.startSupport` que ya acepta agent/customer). Aqui pasamos el
  // opener como customer y un agent placeholder = agentTenantId como userId
  // hasta que el claim reemplace su identidad.
  const ticketId = randomUUID();
  const conversationResult = Conversation.startSupport({
    tenantId: cmd.tenantId,
    ticketId,
    agent: { userId: agentTenantId, displayName: 'Support Team', actorType: 'PlatformAdmin' },
    customer: cmd.opener,
  });
  if (!conversationResult.isSuccess) return Result.fail(conversationResult.error);

  const ticketResult = SupportTicket.open({
    tenantId: cmd.tenantId,
    agentTenantId,
    openedByUserId: cmd.opener.userId,
    conversationId: conversationResult.value.id,
    subject: cmd.subject,
    category: cmd.category,
    ...(cmd.priority !== undefined ? { priority: cmd.priority } : {}),
  });
  if (!ticketResult.isSuccess) return Result.fail(ticketResult.error);
  const ticket = ticketResult.value;

  const conversation = conversationResult.value;
  if (cmd.initialMessage && cmd.initialMessage.trim().length > 0) {
    const initial = conversation.sendText({ senderId: cmd.opener.userId, body: cmd.initialMessage });
    if (!initial.isSuccess) return Result.fail(initial.error);
  }

  await deps.conversations.save(conversation);
  await deps.supportTickets.save(ticket);

  const snapshot = ticket.toSnapshot();
  const event: SupportOpenedEvent = {
    eventId: randomUUID(),
    eventType: SupportEventTypes.Opened,
    tenantId: cmd.tenantId,
    correlationId: cmd.correlationId,
    occurredOnUtc: snapshot.openedAtUtc.toISOString(),
    ticketId: snapshot.id,
    agentTenantId: snapshot.agentTenantId,
    openedByUserId: snapshot.openedByUserId,
    conversationId: snapshot.conversationId,
    subject: snapshot.subject,
    category: snapshot.category,
    priority: snapshot.priority,
  };
  await deps.publisher.enqueue(event);

  return Result.ok({ ticketId: snapshot.id, conversationId: snapshot.conversationId });
}
