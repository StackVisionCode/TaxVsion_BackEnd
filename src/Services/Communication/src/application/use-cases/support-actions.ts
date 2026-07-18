import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../../domain/shared/result.js';
import type { SupportTicketRepository } from '../ports/support-ticket-repository.js';
import type { IntegrationEventPublisher } from '../ports/integration-event-publisher.js';
import {
  SupportEventTypes,
  type SupportClaimedEvent,
  type SupportClosedEvent,
  type SupportResolvedEvent,
  type SupportReassignedEvent,
  type SupportEscalatedEvent,
  type SupportReopenedEvent,
} from '../../contracts/events/support-events.js';

export interface ReassignSupportTicketCommand {
  readonly correlationId: string;
  readonly ticketId: string;
  readonly actor: {
    readonly userId: string;
    readonly tenantId: string;
    readonly hasAgentPermission: boolean;
    readonly isPlatformAdmin: boolean;
  };
  readonly newAgentUserId: string;
}

export async function reassignSupportTicket(
  cmd: ReassignSupportTicketCommand,
  deps: { supportTickets: SupportTicketRepository; publisher: IntegrationEventPublisher },
): Promise<Result<{ ticketId: string; newAgentId: string }>> {
  const ticket = await deps.supportTickets.findById(cmd.ticketId);
  if (!ticket) return Result.fail(makeError('Support.NotFound', 'Ticket not found.'));

  if (!cmd.actor.hasAgentPermission && !cmd.actor.isPlatformAdmin) {
    return Result.fail(makeError('Auth.Forbidden', 'Missing communication.support.agent.'));
  }
  if (cmd.actor.tenantId !== ticket.agentTenantId && !cmd.actor.isPlatformAdmin) {
    return Result.fail(
      makeError('Support.WrongAgentTenant', 'Agent must belong to the platform tenant.'),
    );
  }

  const previousAgentId = ticket.assignedAgentId;
  if (!previousAgentId) {
    return Result.fail(makeError('Support.NotClaimed', 'Ticket must be claimed before reassigning.'));
  }

  const result = ticket.reassign({ newAgentUserId: cmd.newAgentUserId });
  if (!result.isSuccess) return Result.fail(result.error);
  await deps.supportTickets.save(ticket);

  const now = new Date();
  const event: SupportReassignedEvent = {
    eventId: randomUUID(),
    eventType: SupportEventTypes.Reassigned,
    tenantId: ticket.tenantId,
    correlationId: cmd.correlationId,
    occurredOnUtc: now.toISOString(),
    ticketId: ticket.id,
    previousAgentId,
    newAgentId: cmd.newAgentUserId,
    reassignedByUserId: cmd.actor.userId,
    reassignedAtUtc: now.toISOString(),
  };
  await deps.publisher.enqueue(event);
  return Result.ok({ ticketId: ticket.id, newAgentId: cmd.newAgentUserId });
}

export interface EscalateSupportTicketCommand {
  readonly correlationId: string;
  readonly ticketId: string;
  readonly actor: {
    readonly userId: string;
    readonly tenantId: string;
    readonly hasAgentPermission: boolean;
    readonly isPlatformAdmin: boolean;
  };
  readonly newPriority: 'Low' | 'Normal' | 'High' | 'Urgent';
}

export async function escalateSupportTicket(
  cmd: EscalateSupportTicketCommand,
  deps: { supportTickets: SupportTicketRepository; publisher: IntegrationEventPublisher },
): Promise<Result<{ ticketId: string; previousPriority: string; newPriority: string }>> {
  const ticket = await deps.supportTickets.findById(cmd.ticketId);
  if (!ticket) return Result.fail(makeError('Support.NotFound', 'Ticket not found.'));

  if (!cmd.actor.hasAgentPermission && !cmd.actor.isPlatformAdmin) {
    return Result.fail(makeError('Auth.Forbidden', 'Missing communication.support.agent.'));
  }
  if (cmd.actor.tenantId !== ticket.agentTenantId && !cmd.actor.isPlatformAdmin) {
    return Result.fail(
      makeError('Support.WrongAgentTenant', 'Agent must belong to the platform tenant.'),
    );
  }

  const result = ticket.escalate({ escalatedByUserId: cmd.actor.userId, newPriority: cmd.newPriority });
  if (!result.isSuccess) return Result.fail(result.error);
  await deps.supportTickets.save(ticket);

  const now = new Date();
  const event: SupportEscalatedEvent = {
    eventId: randomUUID(),
    eventType: SupportEventTypes.Escalated,
    tenantId: ticket.tenantId,
    correlationId: cmd.correlationId,
    occurredOnUtc: now.toISOString(),
    ticketId: ticket.id,
    escalatedByUserId: cmd.actor.userId,
    previousPriority: result.value.previousPriority,
    newPriority: cmd.newPriority,
    escalatedAtUtc: now.toISOString(),
  };
  await deps.publisher.enqueue(event);
  return Result.ok({ ticketId: ticket.id, previousPriority: result.value.previousPriority, newPriority: cmd.newPriority });
}

export interface ReopenSupportTicketCommand {
  readonly correlationId: string;
  readonly ticketId: string;
  readonly actor: {
    readonly userId: string;
    readonly tenantId: string;
    readonly isPlatformAdmin: boolean;
  };
  readonly reason?: string | null;
}

export async function reopenSupportTicket(
  cmd: ReopenSupportTicketCommand,
  deps: { supportTickets: SupportTicketRepository; publisher: IntegrationEventPublisher },
): Promise<Result<{ ticketId: string }>> {
  const ticket = await deps.supportTickets.findById(cmd.ticketId);
  if (!ticket) return Result.fail(makeError('Support.NotFound', 'Ticket not found.'));

  const canAccess = ticket.canBeAccessedBy({
    actorUserId: cmd.actor.userId,
    actorTenantId: cmd.actor.tenantId,
    actorHasAgentPermission: false,
    isPlatformAdmin: cmd.actor.isPlatformAdmin,
  });
  if (!canAccess) return Result.fail(makeError('Auth.Forbidden', 'Not authorized on this ticket.'));

  const result = ticket.reopen({
    reopenedByUserId: cmd.actor.userId,
    isPlatformAdmin: cmd.actor.isPlatformAdmin,
  });
  if (!result.isSuccess) return Result.fail(result.error);
  await deps.supportTickets.save(ticket);

  const now = new Date();
  const event: SupportReopenedEvent = {
    eventId: randomUUID(),
    eventType: SupportEventTypes.Reopened,
    tenantId: ticket.tenantId,
    correlationId: cmd.correlationId,
    occurredOnUtc: now.toISOString(),
    ticketId: ticket.id,
    reopenedByUserId: cmd.actor.userId,
    reopenedAtUtc: now.toISOString(),
    reason: cmd.reason ?? null,
  };
  await deps.publisher.enqueue(event);
  return Result.ok({ ticketId: ticket.id });
}

export interface ClaimSupportTicketCommand {
  readonly correlationId: string;
  readonly ticketId: string;
  readonly agent: {
    readonly userId: string;
    readonly tenantId: string;
    readonly hasAgentPermission: boolean;
    readonly isPlatformAdmin: boolean;
  };
}

export async function claimSupportTicket(
  cmd: ClaimSupportTicketCommand,
  deps: { supportTickets: SupportTicketRepository; publisher: IntegrationEventPublisher },
): Promise<Result<{ ticketId: string; assignedAgentId: string }>> {
  const ticket = await deps.supportTickets.findById(cmd.ticketId);
  if (!ticket) return Result.fail(makeError('Support.NotFound', 'Ticket not found.'));

  if (!cmd.agent.hasAgentPermission && !cmd.agent.isPlatformAdmin) {
    return Result.fail(makeError('Auth.Forbidden', 'Missing communication.support.agent.'));
  }
  if (cmd.agent.tenantId !== ticket.agentTenantId && !cmd.agent.isPlatformAdmin) {
    return Result.fail(
      makeError('Support.WrongAgentTenant', 'Agent must belong to the platform tenant.'),
    );
  }
  const result = ticket.claim({ agentUserId: cmd.agent.userId });
  if (!result.isSuccess) return Result.fail(result.error);
  await deps.supportTickets.save(ticket);

  const event: SupportClaimedEvent = {
    eventId: randomUUID(),
    eventType: SupportEventTypes.Claimed,
    tenantId: ticket.tenantId,
    correlationId: cmd.correlationId,
    occurredOnUtc: new Date().toISOString(),
    ticketId: ticket.id,
    assignedAgentId: cmd.agent.userId,
    claimedAtUtc: new Date().toISOString(),
  };
  await deps.publisher.enqueue(event);
  return Result.ok({ ticketId: ticket.id, assignedAgentId: cmd.agent.userId });
}

export interface ResolveSupportTicketCommand {
  readonly correlationId: string;
  readonly ticketId: string;
  readonly actor: {
    readonly userId: string;
    readonly tenantId: string;
    readonly hasAgentPermission: boolean;
    readonly isPlatformAdmin: boolean;
  };
}

export async function resolveSupportTicket(
  cmd: ResolveSupportTicketCommand,
  deps: { supportTickets: SupportTicketRepository; publisher: IntegrationEventPublisher },
): Promise<Result<{ ticketId: string }>> {
  const ticket = await deps.supportTickets.findById(cmd.ticketId);
  if (!ticket) return Result.fail(makeError('Support.NotFound', 'Ticket not found.'));

  const isAgent =
    cmd.actor.tenantId === ticket.agentTenantId &&
    (cmd.actor.hasAgentPermission || cmd.actor.isPlatformAdmin);
  const canAccess = ticket.canBeAccessedBy({
    actorUserId: cmd.actor.userId,
    actorTenantId: cmd.actor.tenantId,
    actorHasAgentPermission: cmd.actor.hasAgentPermission,
    isPlatformAdmin: cmd.actor.isPlatformAdmin,
  });
  if (!canAccess) return Result.fail(makeError('Auth.Forbidden', 'Not authorized on this ticket.'));

  const result = ticket.resolve({ byUserId: cmd.actor.userId, isAgent });
  if (!result.isSuccess) return Result.fail(result.error);
  await deps.supportTickets.save(ticket);

  const event: SupportResolvedEvent = {
    eventId: randomUUID(),
    eventType: SupportEventTypes.Resolved,
    tenantId: ticket.tenantId,
    correlationId: cmd.correlationId,
    occurredOnUtc: new Date().toISOString(),
    ticketId: ticket.id,
    resolvedByUserId: cmd.actor.userId,
    resolvedByAgent: isAgent,
    resolvedAtUtc: new Date().toISOString(),
  };
  await deps.publisher.enqueue(event);
  return Result.ok({ ticketId: ticket.id });
}

export interface CloseSupportTicketCommand {
  readonly correlationId: string;
  readonly ticketId: string;
  readonly actor: {
    readonly userId: string;
    readonly tenantId: string;
    readonly hasAgentPermission: boolean;
    readonly isPlatformAdmin: boolean;
  };
}

export async function closeSupportTicket(
  cmd: CloseSupportTicketCommand,
  deps: { supportTickets: SupportTicketRepository; publisher: IntegrationEventPublisher },
): Promise<Result<{ ticketId: string }>> {
  const ticket = await deps.supportTickets.findById(cmd.ticketId);
  if (!ticket) return Result.fail(makeError('Support.NotFound', 'Ticket not found.'));

  const canAccess = ticket.canBeAccessedBy({
    actorUserId: cmd.actor.userId,
    actorTenantId: cmd.actor.tenantId,
    actorHasAgentPermission: cmd.actor.hasAgentPermission,
    isPlatformAdmin: cmd.actor.isPlatformAdmin,
  });
  if (!canAccess) return Result.fail(makeError('Auth.Forbidden', 'Not authorized on this ticket.'));

  const result = ticket.close({ byUserId: cmd.actor.userId, isPlatformAdmin: cmd.actor.isPlatformAdmin });
  if (!result.isSuccess) return Result.fail(result.error);
  await deps.supportTickets.save(ticket);

  const event: SupportClosedEvent = {
    eventId: randomUUID(),
    eventType: SupportEventTypes.Closed,
    tenantId: ticket.tenantId,
    correlationId: cmd.correlationId,
    occurredOnUtc: new Date().toISOString(),
    ticketId: ticket.id,
    closedByUserId: cmd.actor.userId,
    closedAtUtc: new Date().toISOString(),
  };
  await deps.publisher.enqueue(event);
  return Result.ok({ ticketId: ticket.id });
}
