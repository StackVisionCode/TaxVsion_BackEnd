import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { SupportTicket } from '../../src/domain/support/support-ticket.js';
import {
  claimSupportTicket,
  closeSupportTicket,
  resolveSupportTicket,
} from '../../src/application/use-cases/support-actions.js';
import type { SupportTicketRepository } from '../../src/application/ports/support-ticket-repository.js';
import type { IntegrationEvent } from '../../src/contracts/events/integration-event.js';

const u = () => randomUUID();

function setup() {
  const tenantId = u();
  const agentTenantId = u();
  const openedByUserId = u();
  const conversationId = u();
  const opened = SupportTicket.open({ tenantId, agentTenantId, openedByUserId, conversationId, subject: 'Printer' });
  if (!opened.isSuccess) throw new Error(opened.error.message);
  const ticket = opened.value;
  const published: IntegrationEvent[] = [];
  const deps = {
    supportTickets: {
      findById: async (id: string) => (id === ticket.id ? ticket : null),
      save: async () => undefined,
    } as unknown as SupportTicketRepository,
    publisher: { enqueue: async (e: IntegrationEvent) => void published.push(e) },
  };
  const agent = { userId: u(), tenantId: agentTenantId, hasAgentPermission: true, isPlatformAdmin: false };
  const expected = { openedByUserId, agentTenantId, conversationId };
  return { ticket, deps, agent, published, expected };
}

describe('support actions — evento con quien abrió', () => {
  it('claimed event carries who opened the ticket', async () => {
    const { ticket, deps, agent, published, expected } = setup();

    const result = await claimSupportTicket({ correlationId: 'c', ticketId: ticket.id, agent }, deps);

    expect(result.isSuccess).toBe(true);
    expect(published.at(-1)).toMatchObject({ eventType: 'communication.support.claimed.v1', ...expected });
  });

  it('resolved and closed events carry who opened the ticket', async () => {
    const { ticket, deps, agent, published, expected } = setup();
    await claimSupportTicket({ correlationId: 'c', ticketId: ticket.id, agent }, deps);

    expect((await resolveSupportTicket({ correlationId: 'c', ticketId: ticket.id, actor: agent }, deps)).isSuccess).toBe(true);
    expect(published.at(-1)).toMatchObject({ eventType: 'communication.support.resolved.v1', ...expected });

    expect((await closeSupportTicket({ correlationId: 'c', ticketId: ticket.id, actor: agent }, deps)).isSuccess).toBe(true);
    expect(published.at(-1)).toMatchObject({ eventType: 'communication.support.closed.v1', ...expected });
  });
});
