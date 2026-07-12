import { Result } from '../../domain/shared/result.js';
import type { SupportTicketRepository } from '../ports/support-ticket-repository.js';

export interface SupportTicketDto {
  id: string;
  tenantId: string;
  agentTenantId: string;
  openedByUserId: string;
  assignedAgentId: string | null;
  conversationId: string;
  subject: string;
  category: 'Billing' | 'Technical' | 'Account' | 'Other';
  priority: 'Low' | 'Normal' | 'High' | 'Urgent';
  status: 'Open' | 'Claimed' | 'WaitingCustomer' | 'WaitingAgent' | 'Resolved' | 'Closed';
  openedAtUtc: string;
  claimedAtUtc: string | null;
  resolvedAtUtc: string | null;
  closedAtUtc: string | null;
}

export interface ListForCustomerQuery {
  readonly tenantId: string;
  readonly openedByUserId: string;
  readonly page: number;
  readonly size: number;
  readonly includeClosed?: boolean;
}

export interface ListForAgentQuery {
  readonly agentTenantId: string;
  readonly assignedAgentId?: string | null;
  readonly page: number;
  readonly size: number;
  readonly includeClosed?: boolean;
}

export interface ListResult {
  readonly items: readonly SupportTicketDto[];
  readonly page: number;
  readonly size: number;
  readonly totalCount: number;
}

function toDto(snap: {
  id: string;
  tenantId: string;
  agentTenantId: string;
  openedByUserId: string;
  assignedAgentId: string | null;
  conversationId: string;
  subject: string;
  category: string;
  priority: string;
  status: string;
  openedAtUtc: Date;
  claimedAtUtc: Date | null;
  resolvedAtUtc: Date | null;
  closedAtUtc: Date | null;
}): SupportTicketDto {
  return {
    id: snap.id,
    tenantId: snap.tenantId,
    agentTenantId: snap.agentTenantId,
    openedByUserId: snap.openedByUserId,
    assignedAgentId: snap.assignedAgentId,
    conversationId: snap.conversationId,
    subject: snap.subject,
    category: snap.category as SupportTicketDto['category'],
    priority: snap.priority as SupportTicketDto['priority'],
    status: snap.status as SupportTicketDto['status'],
    openedAtUtc: snap.openedAtUtc.toISOString(),
    claimedAtUtc: snap.claimedAtUtc ? snap.claimedAtUtc.toISOString() : null,
    resolvedAtUtc: snap.resolvedAtUtc ? snap.resolvedAtUtc.toISOString() : null,
    closedAtUtc: snap.closedAtUtc ? snap.closedAtUtc.toISOString() : null,
  };
}

export async function listSupportTicketsForCustomer(
  q: ListForCustomerQuery,
  deps: { supportTickets: SupportTicketRepository },
): Promise<Result<ListResult>> {
  const size = Math.min(Math.max(q.size, 1), 100);
  const page = Math.max(q.page, 1);
  const [items, totalCount] = await Promise.all([
    deps.supportTickets.listForCustomer({
      tenantId: q.tenantId,
      openedByUserId: q.openedByUserId,
      take: size,
      skip: (page - 1) * size,
      ...(q.includeClosed !== undefined ? { includeClosed: q.includeClosed } : {}),
    }),
    deps.supportTickets.countForCustomer(q.tenantId, q.openedByUserId, q.includeClosed ?? false),
  ]);
  return Result.ok({ items: items.map(toDto), page, size, totalCount });
}

export async function listSupportTicketsForAgent(
  q: ListForAgentQuery,
  deps: { supportTickets: SupportTicketRepository },
): Promise<Result<ListResult>> {
  const size = Math.min(Math.max(q.size, 1), 100);
  const page = Math.max(q.page, 1);
  const [items, totalCount] = await Promise.all([
    deps.supportTickets.listForAgentTenant({
      agentTenantId: q.agentTenantId,
      assignedAgentId: q.assignedAgentId ?? null,
      take: size,
      skip: (page - 1) * size,
      ...(q.includeClosed !== undefined ? { includeClosed: q.includeClosed } : {}),
    }),
    deps.supportTickets.countForAgentTenant(
      q.agentTenantId,
      q.assignedAgentId ?? null,
      q.includeClosed ?? false,
    ),
  ]);
  return Result.ok({ items: items.map(toDto), page, size, totalCount });
}
