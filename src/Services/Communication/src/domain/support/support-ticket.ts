import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../shared/result.js';
import {
  SupportCategory,
  SupportPriority,
  SupportStatus,
  isTerminalSupport,
} from './support-enums.js';

export interface SupportTicketSnapshot {
  readonly id: string;
  readonly tenantId: string;
  readonly agentTenantId: string;
  readonly openedByUserId: string;
  readonly assignedAgentId: string | null;
  readonly conversationId: string;
  readonly subject: string;
  readonly category: SupportCategory;
  readonly priority: SupportPriority;
  readonly status: SupportStatus;
  readonly openedAtUtc: Date;
  readonly claimedAtUtc: Date | null;
  readonly resolvedAtUtc: Date | null;
  readonly closedAtUtc: Date | null;
  readonly updatedAtUtc: Date;
}

export class SupportTicket {
  private constructor(private state: SupportTicketSnapshot) {}

  static rehydrate(snapshot: SupportTicketSnapshot): SupportTicket {
    return new SupportTicket(snapshot);
  }

  static open(input: {
    tenantId: string;
    agentTenantId: string;
    openedByUserId: string;
    conversationId: string;
    subject: string;
    category?: SupportCategory;
    priority?: SupportPriority;
    now?: Date;
  }): Result<SupportTicket> {
    if (input.subject.trim().length === 0) {
      return Result.fail(makeError('Support.MissingSubject', 'Subject is required.'));
    }
    if (input.tenantId === input.agentTenantId) {
      return Result.fail(
        makeError(
          'Support.SameTenant',
          'Support tickets must span customer tenant and platform tenant.',
        ),
      );
    }
    const now = input.now ?? new Date();
    return Result.ok(
      new SupportTicket({
        id: randomUUID(),
        tenantId: input.tenantId,
        agentTenantId: input.agentTenantId,
        openedByUserId: input.openedByUserId,
        assignedAgentId: null,
        conversationId: input.conversationId,
        subject: input.subject.trim().slice(0, 200),
        category: input.category ?? SupportCategory.Other,
        priority: input.priority ?? SupportPriority.Normal,
        status: SupportStatus.Open,
        openedAtUtc: now,
        claimedAtUtc: null,
        resolvedAtUtc: null,
        closedAtUtc: null,
        updatedAtUtc: now,
      }),
    );
  }

  claim(input: { agentUserId: string; now?: Date }): Result<void> {
    if (this.state.status !== SupportStatus.Open) {
      return Result.fail(makeError('Support.InvalidTransition', `Cannot claim from ${this.state.status}.`));
    }
    const now = input.now ?? new Date();
    this.state = {
      ...this.state,
      assignedAgentId: input.agentUserId,
      status: SupportStatus.Claimed,
      claimedAtUtc: now,
      updatedAtUtc: now,
    };
    return Result.okVoid();
  }

  reassign(input: { newAgentUserId: string; now?: Date }): Result<void> {
    if (this.state.status !== SupportStatus.Claimed && this.state.status !== SupportStatus.WaitingCustomer && this.state.status !== SupportStatus.WaitingAgent) {
      return Result.fail(makeError('Support.InvalidTransition', `Cannot reassign from ${this.state.status}.`));
    }
    const now = input.now ?? new Date();
    this.state = {
      ...this.state,
      assignedAgentId: input.newAgentUserId,
      updatedAtUtc: now,
    };
    return Result.okVoid();
  }

  markWaitingCustomer(now: Date = new Date()): Result<void> {
    if (this.state.assignedAgentId === null) {
      return Result.fail(makeError('Support.NotAssigned', 'Ticket must be claimed first.'));
    }
    if (isTerminalSupport(this.state.status)) {
      return Result.fail(makeError('Support.Terminal', 'Ticket is terminal.'));
    }
    this.state = { ...this.state, status: SupportStatus.WaitingCustomer, updatedAtUtc: now };
    return Result.okVoid();
  }

  markWaitingAgent(now: Date = new Date()): Result<void> {
    if (isTerminalSupport(this.state.status)) {
      return Result.fail(makeError('Support.Terminal', 'Ticket is terminal.'));
    }
    this.state = { ...this.state, status: SupportStatus.WaitingAgent, updatedAtUtc: now };
    return Result.okVoid();
  }

  resolve(input: { byUserId: string; isAgent: boolean; now?: Date }): Result<void> {
    if (isTerminalSupport(this.state.status)) {
      return Result.fail(makeError('Support.AlreadyTerminal', `Already ${this.state.status}.`));
    }
    if (input.isAgent && this.state.assignedAgentId !== input.byUserId) {
      return Result.fail(
        makeError('Support.NotAssignedAgent', 'Only the assigned agent can resolve as agent.'),
      );
    }
    if (!input.isAgent && input.byUserId !== this.state.openedByUserId) {
      return Result.fail(
        makeError('Support.NotOpener', 'Only the opener can resolve as customer.'),
      );
    }
    const now = input.now ?? new Date();
    this.state = { ...this.state, status: SupportStatus.Resolved, resolvedAtUtc: now, updatedAtUtc: now };
    return Result.okVoid();
  }

  close(input: { byUserId: string; isPlatformAdmin: boolean; now?: Date }): Result<void> {
    if (this.state.status === SupportStatus.Closed) return Result.okVoid();
    if (
      !input.isPlatformAdmin &&
      input.byUserId !== this.state.openedByUserId &&
      input.byUserId !== this.state.assignedAgentId
    ) {
      return Result.fail(
        makeError('Support.CloseForbidden', 'Only opener, assigned agent or PlatformAdmin can close.'),
      );
    }
    const now = input.now ?? new Date();
    this.state = {
      ...this.state,
      status: SupportStatus.Closed,
      closedAtUtc: now,
      updatedAtUtc: now,
    };
    return Result.okVoid();
  }

  escalate(input: {
    escalatedByUserId: string;
    newPriority: SupportPriority;
    now?: Date;
  }): Result<{ previousPriority: SupportPriority }> {
    if (isTerminalSupport(this.state.status)) {
      return Result.fail(makeError('Support.Terminal', 'Cannot escalate a terminal ticket.'));
    }
    if (this.state.priority === input.newPriority) {
      return Result.fail(makeError('Support.SamePriority', 'New priority is the same as current.'));
    }
    const previousPriority = this.state.priority;
    const now = input.now ?? new Date();
    this.state = { ...this.state, priority: input.newPriority, updatedAtUtc: now };
    return Result.ok({ previousPriority });
  }

  reopen(input: {
    reopenedByUserId: string;
    isPlatformAdmin: boolean;
    now?: Date;
  }): Result<void> {
    if (this.state.status !== SupportStatus.Resolved && this.state.status !== SupportStatus.Closed) {
      return Result.fail(makeError('Support.NotTerminal', `Cannot reopen from ${this.state.status}.`));
    }
    const isAssignedAgent = input.reopenedByUserId === this.state.assignedAgentId;
    const isOpener = input.reopenedByUserId === this.state.openedByUserId;
    if (!isOpener && !isAssignedAgent && !input.isPlatformAdmin) {
      return Result.fail(
        makeError('Support.ReopenForbidden', 'Only opener, assigned agent, or PlatformAdmin can reopen.'),
      );
    }
    const now = input.now ?? new Date();
    const nextStatus = this.state.assignedAgentId ? SupportStatus.Claimed : SupportStatus.Open;
    this.state = {
      ...this.state,
      status: nextStatus,
      resolvedAtUtc: null,
      closedAtUtc: null,
      updatedAtUtc: now,
    };
    return Result.okVoid();
  }

  toSnapshot(): SupportTicketSnapshot {
    return this.state;
  }

  get id(): string {
    return this.state.id;
  }
  get tenantId(): string {
    return this.state.tenantId;
  }
  get agentTenantId(): string {
    return this.state.agentTenantId;
  }
  get openedByUserId(): string {
    return this.state.openedByUserId;
  }
  get assignedAgentId(): string | null {
    return this.state.assignedAgentId;
  }
  get status(): SupportStatus {
    return this.state.status;
  }
  get conversationId(): string {
    return this.state.conversationId;
  }

  /**
   * Puede el actor acceder a este ticket? Regla:
   *   - El opener siempre puede (tenant match).
   *   - El agente asignado siempre puede (aunque venga desde AgentTenantId).
   *   - Un actor con `communication.support.agent` que aun no reclamo puede
   *     LEER (para tomarlo). El caller pasa esa señal.
   *   - PlatformAdmin siempre puede.
   */
  canBeAccessedBy(input: {
    actorUserId: string;
    actorTenantId: string;
    actorHasAgentPermission: boolean;
    isPlatformAdmin: boolean;
  }): boolean {
    if (input.isPlatformAdmin) return true;
    if (input.actorUserId === this.state.openedByUserId && input.actorTenantId === this.state.tenantId) {
      return true;
    }
    if (input.actorUserId === this.state.assignedAgentId && input.actorTenantId === this.state.agentTenantId) {
      return true;
    }
    if (
      input.actorHasAgentPermission &&
      input.actorTenantId === this.state.agentTenantId &&
      this.state.status === SupportStatus.Open
    ) {
      return true;
    }
    return false;
  }
}
