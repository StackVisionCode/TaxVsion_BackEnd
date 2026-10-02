import { randomUUID } from 'node:crypto';
import { describe, expect, it, vi } from 'vitest';
import type { SupportTicketSnapshot } from '../../src/domain/support/support-ticket.js';
import { SupportTicket } from '../../src/domain/support/support-ticket.js';
import type { SupportTicketRepository } from '../../src/application/ports/support-ticket-repository.js';
import type { UserDirectoryRepository } from '../../src/application/ports/user-directory-repository.js';
import {
  getSupportTicket,
  listSupportTicketsForAgent,
} from '../../src/application/use-cases/support-queries.js';

function u(): string {
  return randomUUID();
}

function ticket(overrides: Partial<SupportTicketSnapshot> = {}): SupportTicketSnapshot {
  const now = new Date('2026-10-02T00:00:00.000Z');
  return {
    id: u(),
    tenantId: u(),
    agentTenantId: u(),
    openedByUserId: u(),
    assignedAgentId: u(),
    conversationId: u(),
    subject: 'Unable to upload W-2 document',
    category: 'Technical',
    priority: 'High',
    status: 'Claimed',
    openedAtUtc: now,
    claimedAtUtc: now,
    resolvedAtUtc: null,
    closedAtUtc: null,
    updatedAtUtc: now,
    ...overrides,
  };
}

describe('support queries', () => {
  it('hydrates support ticket list with requester and assigned agent directory projections', async () => {
    const requesterUserId = u();
    const assignedAgentId = u();
    const agentTenantId = u();
    const item = ticket({ agentTenantId, openedByUserId: requesterUserId, assignedAgentId });

    const supportTickets = {
      listForAgentTenant: vi.fn().mockResolvedValue([item]),
      countForAgentTenant: vi.fn().mockResolvedValue(1),
    } as unknown as SupportTicketRepository;

    const userDirectory = {
      findByUserId: vi.fn(),
      findByUserIds: vi.fn().mockResolvedValue([
        {
          userId: requesterUserId,
          tenantId: item.tenantId,
          displayName: 'Manuel Mena',
          email: 'manuel@example.com',
          isActive: true,
          actorType: 'TenantAdmin',
          updatedAtUtc: new Date(),
        },
        {
          userId: assignedAgentId,
          tenantId: agentTenantId,
          displayName: 'Carlos Castillo',
          email: 'carlos@example.com',
          isActive: true,
          actorType: 'PlatformAdmin',
          updatedAtUtc: new Date(),
        },
      ]),
      upsert: vi.fn(),
      markInactive: vi.fn(),
      markActive: vi.fn(),
      searchByDisplayNameOrEmail: vi.fn(),
    } satisfies UserDirectoryRepository;

    const result = await listSupportTicketsForAgent(
      { agentTenantId, page: 1, size: 20 },
      { supportTickets, userDirectory },
    );

    expect(result.isSuccess).toBe(true);
    if (!result.isSuccess) return;
    expect(result.value.items[0]).toMatchObject({
      openedByUserId: requesterUserId,
      requesterDisplayName: 'Manuel Mena',
      requesterEmail: 'manuel@example.com',
      requesterActorType: 'TenantAdmin',
      assignedAgentId,
      assignedAgentDisplayName: 'Carlos Castillo',
      assignedAgentEmail: 'carlos@example.com',
    });
    expect(userDirectory.findByUserIds).toHaveBeenCalledWith([requesterUserId, assignedAgentId]);
  });

  it('limits a non-admin agent list to owned tickets and open unassigned tickets', async () => {
    const actorUserId = u();
    const agentTenantId = u();

    const supportTickets = {
      listForAgentTenant: vi.fn().mockResolvedValue([]),
      countForAgentTenant: vi.fn().mockResolvedValue(0),
    } as unknown as SupportTicketRepository;

    const userDirectory = {
      findByUserId: vi.fn(),
      findByUserIds: vi.fn().mockResolvedValue([]),
      upsert: vi.fn(),
      markInactive: vi.fn(),
      markActive: vi.fn(),
      searchByDisplayNameOrEmail: vi.fn(),
    } satisfies UserDirectoryRepository;

    const result = await listSupportTicketsForAgent(
      { agentTenantId, actorUserId, isPlatformAdmin: false, page: 1, size: 20 },
      { supportTickets, userDirectory },
    );

    expect(result.isSuccess).toBe(true);
    expect(supportTickets.listForAgentTenant).toHaveBeenCalledWith(
      expect.objectContaining({ agentTenantId, assignedAgentId: null, visibleToAgentUserId: actorUserId }),
    );
    expect(supportTickets.countForAgentTenant).toHaveBeenCalledWith(
      agentTenantId,
      null,
      false,
      actorUserId,
    );
  });

  it('returns a hydrated detail only when the actor can access the ticket', async () => {
    const requesterUserId = u();
    const assignedAgentId = u();
    const agentTenantId = u();
    const snapshot = ticket({ agentTenantId, openedByUserId: requesterUserId, assignedAgentId });

    const supportTickets = {
      findById: vi.fn().mockResolvedValue(SupportTicket.rehydrate(snapshot)),
    } as unknown as SupportTicketRepository;

    const userDirectory = {
      findByUserId: vi.fn(),
      findByUserIds: vi.fn().mockResolvedValue([
        {
          userId: requesterUserId,
          tenantId: snapshot.tenantId,
          displayName: 'Manuel Mena',
          email: 'manuel@example.com',
          isActive: true,
          actorType: 'TenantAdmin',
          updatedAtUtc: new Date(),
        },
        {
          userId: assignedAgentId,
          tenantId: agentTenantId,
          displayName: 'Carlos Castillo',
          email: 'carlos@example.com',
          isActive: true,
          actorType: 'PlatformAdmin',
          updatedAtUtc: new Date(),
        },
      ]),
      upsert: vi.fn(),
      markInactive: vi.fn(),
      markActive: vi.fn(),
      searchByDisplayNameOrEmail: vi.fn(),
    } satisfies UserDirectoryRepository;

    const allowed = await getSupportTicket(
      {
        ticketId: snapshot.id,
        actor: {
          userId: assignedAgentId,
          tenantId: agentTenantId,
          hasAgentPermission: true,
          isPlatformAdmin: false,
        },
      },
      { supportTickets, userDirectory },
    );

    expect(allowed.isSuccess).toBe(true);
    if (!allowed.isSuccess) return;
    expect(allowed.value.requesterDisplayName).toBe('Manuel Mena');
    expect(allowed.value.assignedAgentDisplayName).toBe('Carlos Castillo');

    const denied = await getSupportTicket(
      {
        ticketId: snapshot.id,
        actor: {
          userId: u(),
          tenantId: agentTenantId,
          hasAgentPermission: true,
          isPlatformAdmin: false,
        },
      },
      { supportTickets, userDirectory },
    );

    expect(denied.isSuccess).toBe(false);
    if (denied.isSuccess) return;
    expect(denied.error.code).toBe('Auth.Forbidden');
  });
});
