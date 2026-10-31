import { randomUUID } from 'node:crypto';
import { describe, expect, it, vi } from 'vitest';
import { SupportTicket } from '../../src/domain/support/support-ticket.js';
import { openSupportTicket, type OpenSupportTicketDeps } from '../../src/application/use-cases/open-support-ticket.js';
import { reopenSupportTicket } from '../../src/application/use-cases/support-actions.js';
import type { SupportTicketRepository } from '../../src/application/ports/support-ticket-repository.js';

/**
 * El socket se une a sus salas solo al conectar. Un ticket abierto (o reabierto) después por HTTP tiene que unir los
 * sockets de quien lo abrió a la sala; si no, las respuestas de soporte no llegan en vivo hasta reconectar.
 */
describe('Support: sala del chat del ticket en vivo', () => {
  it('abrir un ticket une los sockets de quien lo abrió a la sala de su conversación', async () => {
    const tenantId = randomUUID();
    const opener = randomUUID();
    const emitter = { addUserToConversation: vi.fn() };
    const deps = {
      conversations: { save: vi.fn() },
      supportTickets: { save: vi.fn() },
      publisher: { enqueue: vi.fn() },
      platform: { getPlatformTenantId: () => randomUUID() },
      settings: { get: vi.fn().mockResolvedValue({ chatEnabled: true }) },
      userPermissions: { findActiveSupportRecipients: vi.fn().mockResolvedValue([]) },
      emitter,
    } as unknown as OpenSupportTicketDeps;

    const result = await openSupportTicket(
      {
        tenantId,
        correlationId: 'corr',
        opener: { userId: opener, displayName: 'Ana', actorType: 'TenantAdmin' },
        subject: 'Push notifications',
        category: 'Technical',
      },
      deps,
    );

    expect(result.isSuccess).toBe(true);
    if (!result.isSuccess) return;
    expect(emitter.addUserToConversation).toHaveBeenCalledWith({
      tenantId,
      userId: opener,
      conversationId: result.value.conversationId,
    });
  });

  it('reabrir un ticket cerrado vuelve a unir a quien lo abrió', async () => {
    const tenantId = randomUUID();
    const opener = randomUUID();
    const conversationId = randomUUID();
    const now = new Date('2026-10-02T00:00:00Z');
    const ticket = SupportTicket.rehydrate({
      id: randomUUID(),
      tenantId,
      agentTenantId: randomUUID(),
      openedByUserId: opener,
      assignedAgentId: null,
      conversationId,
      subject: 'Account',
      category: 'Account',
      priority: 'Normal',
      status: 'Closed',
      openedAtUtc: now,
      claimedAtUtc: null,
      resolvedAtUtc: now,
      closedAtUtc: now,
      updatedAtUtc: now,
    });
    const emitter = { addUserToConversation: vi.fn() };
    const supportTickets = { findById: vi.fn().mockResolvedValue(ticket), save: vi.fn() } as unknown as SupportTicketRepository;

    const result = await reopenSupportTicket(
      { correlationId: 'corr', ticketId: ticket.id, actor: { userId: opener, tenantId, isPlatformAdmin: false } },
      { supportTickets, publisher: { enqueue: vi.fn() } as never, emitter },
    );

    expect(result.isSuccess).toBe(true);
    expect(emitter.addUserToConversation).toHaveBeenCalledWith({ tenantId, userId: opener, conversationId });
  });
});
