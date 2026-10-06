import { Result } from '../../domain/shared/result.js';
import type { SupportTicketRepository } from '../ports/support-ticket-repository.js';
import type {
  UserDirectoryEntrySnapshot,
  UserDirectoryRepository,
} from '../ports/user-directory-repository.js';

export interface SupportTicketDto {
  id: string;
  tenantId: string;
  agentTenantId: string;
  openedByUserId: string;
  requesterDisplayName: string | null;
  requesterEmail: string | null;
  requesterActorType: string | null;
  assignedAgentId: string | null;
  assignedAgentDisplayName: string | null;
  assignedAgentEmail: string | null;
  conversationId: string;
  subject: string;
  category: 'Billing' | 'Technical' | 'Account' | 'Other';
  priority: 'Low' | 'Normal' | 'High' | 'Urgent';
  status: 'Open' | 'Claimed' | 'WaitingCustomer' | 'WaitingAgent' | 'Resolved' | 'Closed';
  openedAtUtc: string;
  claimedAtUtc: string | null;
  resolvedAtUtc: string | null;
  closedAtUtc: string | null;
  updatedAtUtc: string;
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
  readonly actorUserId?: string | null;
  readonly isPlatformAdmin?: boolean;
  readonly assignedAgentId?: string | null;
  readonly page: number;
  readonly size: number;
  readonly includeClosed?: boolean;
}

export interface GetSupportTicketQuery {
  readonly ticketId: string;
  readonly actor: {
    readonly userId: string;
    readonly tenantId: string;
    readonly hasAgentPermission: boolean;
    readonly isPlatformAdmin: boolean;
  };
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
  updatedAtUtc: Date;
}, users: ReadonlyMap<string, UserDirectoryEntrySnapshot>): SupportTicketDto {
  const requester = users.get(snap.openedByUserId) ?? null;
  const assignedAgent = snap.assignedAgentId ? users.get(snap.assignedAgentId) ?? null : null;
  return {
    id: snap.id,
    tenantId: snap.tenantId,
    agentTenantId: snap.agentTenantId,
    openedByUserId: snap.openedByUserId,
    requesterDisplayName: requester?.displayName ?? null,
    requesterEmail: requester?.email ?? null,
    requesterActorType: requester?.actorType ?? null,
    assignedAgentId: snap.assignedAgentId,
    assignedAgentDisplayName: assignedAgent?.displayName ?? null,
    assignedAgentEmail: assignedAgent?.email ?? null,
    conversationId: snap.conversationId,
    subject: snap.subject,
    category: snap.category as SupportTicketDto['category'],
    priority: snap.priority as SupportTicketDto['priority'],
    status: snap.status as SupportTicketDto['status'],
    openedAtUtc: snap.openedAtUtc.toISOString(),
    claimedAtUtc: snap.claimedAtUtc ? snap.claimedAtUtc.toISOString() : null,
    resolvedAtUtc: snap.resolvedAtUtc ? snap.resolvedAtUtc.toISOString() : null,
    closedAtUtc: snap.closedAtUtc ? snap.closedAtUtc.toISOString() : null,
    updatedAtUtc: snap.updatedAtUtc.toISOString(),
  };
}

export async function listSupportTicketsForCustomer(
  q: ListForCustomerQuery,
  deps: { supportTickets: SupportTicketRepository; userDirectory: UserDirectoryRepository },
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
  const users = await readTicketUsers(items, deps.userDirectory);
  return Result.ok({ items: items.map((item) => toDto(item, users)), page, size, totalCount });
}

export async function listSupportTicketsForAgent(
  q: ListForAgentQuery,
  deps: { supportTickets: SupportTicketRepository; userDirectory: UserDirectoryRepository },
): Promise<Result<ListResult>> {
  const size = Math.min(Math.max(q.size, 1), 100);
  const page = Math.max(q.page, 1);
  const visibleToAgentUserId =
    q.assignedAgentId === undefined && q.isPlatformAdmin !== true ? q.actorUserId ?? null : null;
  const [items, totalCount] = await Promise.all([
    deps.supportTickets.listForAgentTenant({
      agentTenantId: q.agentTenantId,
      assignedAgentId: q.assignedAgentId ?? null,
      visibleToAgentUserId,
      take: size,
      skip: (page - 1) * size,
      ...(q.includeClosed !== undefined ? { includeClosed: q.includeClosed } : {}),
    }),
    deps.supportTickets.countForAgentTenant(
      q.agentTenantId,
      q.assignedAgentId ?? null,
      q.includeClosed ?? false,
      visibleToAgentUserId,
    ),
  ]);
  const users = await readTicketUsers(items, deps.userDirectory);
  return Result.ok({ items: items.map((item) => toDto(item, users)), page, size, totalCount });
}

export async function getSupportTicket(
  q: GetSupportTicketQuery,
  deps: { supportTickets: SupportTicketRepository; userDirectory: UserDirectoryRepository },
): Promise<Result<SupportTicketDto>> {
  const ticket = await deps.supportTickets.findById(q.ticketId);
  if (!ticket) {
    return Result.fail({ code: 'Support.NotFound', message: 'Ticket not found.' });
  }

  const canAccess = ticket.canBeAccessedBy({
    actorUserId: q.actor.userId,
    actorTenantId: q.actor.tenantId,
    actorHasAgentPermission: q.actor.hasAgentPermission,
    isPlatformAdmin: q.actor.isPlatformAdmin,
  });
  if (!canAccess) {
    return Result.fail({ code: 'Auth.Forbidden', message: 'Not authorized on this ticket.' });
  }

  const snapshot = ticket.toSnapshot();
  const users = await readTicketUsers([snapshot], deps.userDirectory);
  return Result.ok(toDto(snapshot, users));
}

async function readTicketUsers(
  tickets: readonly {
    openedByUserId: string;
    assignedAgentId: string | null;
  }[],
  userDirectory: UserDirectoryRepository,
): Promise<ReadonlyMap<string, UserDirectoryEntrySnapshot>> {
  const userIds = [
    ...new Set(
      tickets.flatMap((ticket) =>
        ticket.assignedAgentId ? [ticket.openedByUserId, ticket.assignedAgentId] : [ticket.openedByUserId],
      ),
    ),
  ];

  if (userIds.length === 0) {
    return new Map();
  }

  const entries = userDirectory.findByUserIds
    ? await userDirectory.findByUserIds(userIds)
    : await Promise.all(userIds.map((userId) => userDirectory.findByUserId(userId))).then((items) =>
        items.filter((item): item is UserDirectoryEntrySnapshot => item !== null),
      );

  return new Map(entries.map((entry) => [entry.userId, entry]));
}
