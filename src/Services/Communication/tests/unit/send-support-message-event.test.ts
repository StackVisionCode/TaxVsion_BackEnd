import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { sendMessage } from '../../src/application/use-cases/send-message.js';
import type { Conversation } from '../../src/domain/conversations/conversation.js';
import type { ConversationRepository } from '../../src/application/ports/conversation-repository.js';
import type { SupportTicketRepository } from '../../src/application/ports/support-ticket-repository.js';
import type { UserPermissionsProjectionRepository } from '../../src/application/ports/user-permissions-projection-repository.js';
import type { IntegrationEvent } from '../../src/contracts/events/integration-event.js';
import type { SupportMessageAddedEvent } from '../../src/contracts/events/support-events.js';
import { SupportTicket } from '../../src/domain/support/support-ticket.js';
import { conversationWith } from '../helpers/chat-file-library.js';

const u = () => randomUUID();

function setup(opts: { assignedAgentId?: string; staleTicketId?: boolean } = {}) {
  const tenantId = u();
  const agentTenantId = u();
  const openerId = u();
  const conversationId = u();
  const opened = SupportTicket.open({ tenantId, agentTenantId, openedByUserId: openerId, conversationId, subject: 'Printer' });
  if (!opened.isSuccess) throw new Error(opened.error.message);
  const ticket = opened.value;
  if (opts.assignedAgentId) ticket.claim({ agentUserId: opts.assignedAgentId });
  const conversation = conversationWith({
    tenantId,
    kind: 'Support',
    id: conversationId,
    // Tickets abiertos antes del arreglo: la conversación guarda un id que no es el del ticket.
    ticketId: opts.staleTicketId ? u() : ticket.id,
    participants: [{ userId: openerId }, { userId: agentTenantId }],
  });
  const agents = [u(), u()];
  const published: IntegrationEvent[] = [];
  const deps = {
    conversations: {
      findById: async (t: string, id: string) => (t === tenantId && id === conversation.id ? conversation : null),
      save: async (_c: Conversation) => undefined,
    } as unknown as ConversationRepository,
    idempotency: {
      tryReserve: async () => ({ status: 'fresh' as const, token: u() }),
      commit: async () => undefined,
      release: async () => undefined,
    },
    publisher: { enqueue: async (e: IntegrationEvent) => void published.push(e) },
    settings: {
      get: async (t: string) => ({
        tenantId: t,
        chatEnabled: true,
        employeeToEmployeeChatEnabled: true,
        restrictCustomerChatToAssignedPreparer: false,
        screenshotsEnabled: true,
      }),
    },
    attachmentTracking: { register: async () => undefined, markStatus: async () => null, findByFileId: async () => null },
    attachmentReservations: { reserve: async () => undefined, consume: async () => true, purgeOlderThan: async () => 0 },
    supportTickets: {
      findById: async (id: string) => (id === ticket.id ? ticket : null),
      findByConversationId: async (id: string) => (id === conversationId ? ticket : null),
    } as unknown as SupportTicketRepository,
    userPermissions: {
      findActiveSupportRecipients: async (t: string) => (t === agentTenantId ? agents.map((userId) => ({ userId })) : []),
    } as unknown as UserPermissionsProjectionRepository,
  };
  const send = async (senderUserId: string) => {
    const result = await sendMessage(
      { tenantId, correlationId: 'corr', clientKey: u(), conversationId, senderUserId, body: 'hello' },
      deps,
    );
    expect(result.isSuccess).toBe(true);
    return published.find((e): e is SupportMessageAddedEvent => e.eventType === 'communication.support.message_added.v1');
  };
  return { ticket, tenantId, agentTenantId, openerId, conversationId, agents, send };
}

describe('sendMessage — mensaje en un ticket de soporte', () => {
  it('finds the ticket by conversation when the conversation keeps an old ticket id', async () => {
    const { send, openerId, ticket } = setup({ staleTicketId: true });

    const event = await send(openerId);

    expect(event?.ticketId).toBe(ticket.id);
  });

  it('customer message on an unassigned ticket lists the active agents', async () => {
    const { send, openerId, agentTenantId, conversationId, agents, ticket } = setup();

    const event = await send(openerId);

    expect(event).toMatchObject({
      ticketId: ticket.id,
      senderUserId: openerId,
      senderRole: 'Customer',
      conversationId,
      agentTenantId,
      openedByUserId: openerId,
      assignedAgentId: null,
      supportRecipientUserIds: agents,
    });
  });

  it('customer message on an assigned ticket carries the agent and no list', async () => {
    const agentId = u();
    const { send, openerId } = setup({ assignedAgentId: agentId });

    const event = await send(openerId);

    expect(event?.assignedAgentId).toBe(agentId);
    expect(event?.supportRecipientUserIds).toEqual([]);
  });

  it('agent reply is published with senderRole Agent and no recipient list', async () => {
    const { send, agentTenantId } = setup({ assignedAgentId: u() });

    const event = await send(agentTenantId);

    expect(event?.senderRole).toBe('Agent');
    expect(event?.supportRecipientUserIds).toEqual([]);
  });
});
