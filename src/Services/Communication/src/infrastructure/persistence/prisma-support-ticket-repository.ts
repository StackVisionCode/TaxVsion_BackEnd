import type { PrismaClient } from '@prisma/client';
import { SupportTicket, type SupportTicketSnapshot } from '../../domain/support/support-ticket.js';
import type { SupportTicketRepository } from '../../application/ports/support-ticket-repository.js';
import {
  isSupportCategory,
  isSupportPriority,
  isSupportStatus,
} from '../../domain/support/support-enums.js';

export class PrismaSupportTicketRepository implements SupportTicketRepository {
  constructor(private readonly prisma: PrismaClient) {}

  async save(ticket: SupportTicket): Promise<void> {
    const s = ticket.toSnapshot();
    await this.prisma.supportTicket.upsert({
      where: { Id: s.id },
      create: {
        Id: s.id,
        TenantId: s.tenantId,
        AgentTenantId: s.agentTenantId,
        OpenedByUserId: s.openedByUserId,
        AssignedAgentId: s.assignedAgentId,
        ConversationId: s.conversationId,
        Subject: s.subject,
        Category: s.category,
        Priority: s.priority,
        Status: s.status,
        OpenedAtUtc: s.openedAtUtc,
        ClaimedAtUtc: s.claimedAtUtc,
        ResolvedAtUtc: s.resolvedAtUtc,
        ClosedAtUtc: s.closedAtUtc,
        UpdatedAtUtc: s.updatedAtUtc,
      },
      update: {
        AssignedAgentId: s.assignedAgentId,
        Status: s.status,
        Priority: s.priority,
        ClaimedAtUtc: s.claimedAtUtc,
        ResolvedAtUtc: s.resolvedAtUtc,
        ClosedAtUtc: s.closedAtUtc,
        UpdatedAtUtc: s.updatedAtUtc,
      },
    });
  }

  async findById(id: string): Promise<SupportTicket | null> {
    const row = await this.prisma.supportTicket.findUnique({ where: { Id: id } });
    return row ? SupportTicket.rehydrate(this.toSnapshot(row)) : null;
  }

  async listForCustomer(input: {
    tenantId: string;
    openedByUserId: string;
    take: number;
    skip: number;
    includeClosed?: boolean;
  }): Promise<SupportTicketSnapshot[]> {
    const whereBase = { TenantId: input.tenantId, OpenedByUserId: input.openedByUserId };
    const where = input.includeClosed
      ? whereBase
      : { ...whereBase, Status: { notIn: ['Closed'] } };
    const rows = await this.prisma.supportTicket.findMany({
      where,
      orderBy: { OpenedAtUtc: 'desc' },
      take: input.take,
      skip: input.skip,
    });
    return rows.map((r) => this.toSnapshot(r));
  }

  async countForCustomer(
    tenantId: string,
    openedByUserId: string,
    includeClosed = false,
  ): Promise<number> {
    const whereBase = { TenantId: tenantId, OpenedByUserId: openedByUserId };
    const where = includeClosed ? whereBase : { ...whereBase, Status: { notIn: ['Closed'] } };
    return this.prisma.supportTicket.count({ where });
  }

  async listForAgentTenant(input: {
    agentTenantId: string;
    assignedAgentId?: string | null;
    take: number;
    skip: number;
    includeClosed?: boolean;
  }): Promise<SupportTicketSnapshot[]> {
    const whereBase =
      input.assignedAgentId !== undefined && input.assignedAgentId !== null
        ? { AgentTenantId: input.agentTenantId, AssignedAgentId: input.assignedAgentId }
        : { AgentTenantId: input.agentTenantId };
    const where = input.includeClosed
      ? whereBase
      : { ...whereBase, Status: { notIn: ['Closed'] } };
    const rows = await this.prisma.supportTicket.findMany({
      where,
      orderBy: { OpenedAtUtc: 'desc' },
      take: input.take,
      skip: input.skip,
    });
    return rows.map((r) => this.toSnapshot(r));
  }

  async countForAgentTenant(
    agentTenantId: string,
    assignedAgentId?: string | null,
    includeClosed = false,
  ): Promise<number> {
    const whereBase =
      assignedAgentId !== undefined && assignedAgentId !== null
        ? { AgentTenantId: agentTenantId, AssignedAgentId: assignedAgentId }
        : { AgentTenantId: agentTenantId };
    const where = includeClosed ? whereBase : { ...whereBase, Status: { notIn: ['Closed'] } };
    return this.prisma.supportTicket.count({ where });
  }

  private toSnapshot(row: {
    Id: string;
    TenantId: string;
    AgentTenantId: string;
    OpenedByUserId: string;
    AssignedAgentId: string | null;
    ConversationId: string;
    Subject: string;
    Category: string;
    Priority: string;
    Status: string;
    OpenedAtUtc: Date;
    ClaimedAtUtc: Date | null;
    ResolvedAtUtc: Date | null;
    ClosedAtUtc: Date | null;
    UpdatedAtUtc: Date;
  }): SupportTicketSnapshot {
    if (!isSupportCategory(row.Category)) throw new Error(`Bad category '${row.Category}'`);
    if (!isSupportPriority(row.Priority)) throw new Error(`Bad priority '${row.Priority}'`);
    if (!isSupportStatus(row.Status)) throw new Error(`Bad status '${row.Status}'`);
    return {
      id: row.Id,
      tenantId: row.TenantId,
      agentTenantId: row.AgentTenantId,
      openedByUserId: row.OpenedByUserId,
      assignedAgentId: row.AssignedAgentId,
      conversationId: row.ConversationId,
      subject: row.Subject,
      category: row.Category,
      priority: row.Priority,
      status: row.Status,
      openedAtUtc: row.OpenedAtUtc,
      claimedAtUtc: row.ClaimedAtUtc,
      resolvedAtUtc: row.ResolvedAtUtc,
      closedAtUtc: row.ClosedAtUtc,
      updatedAtUtc: row.UpdatedAtUtc,
    };
  }
}
