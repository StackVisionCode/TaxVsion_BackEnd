import { describe, expect, it, vi } from 'vitest';
import { bindSupportConsumers } from '../../src/application/event-handlers/support-consumers.js';
import type { IncomingEnvelope } from '../../src/application/ports/event-consumer.js';
import type { AnalyticsRepository } from '../../src/application/ports/analytics-repository.js';
import type { RealtimeEmitter } from '../../src/application/ports/realtime-emitter.js';
import type { SupportTicketRepository } from '../../src/application/ports/support-ticket-repository.js';
import type { NotificationRepository } from '../../src/application/ports/notification-repository.js';
import type {
  UserPermissionsProjectionRepository,
  UserPermissionsProjectionSnapshot,
} from '../../src/application/ports/user-permissions-projection-repository.js';
import { SupportSocketEvents } from '../../src/contracts/socket/support-socket-events.js';
import { SupportTicket } from '../../src/domain/support/support-ticket.js';
import type { Notification, NotificationSnapshot } from '../../src/domain/notifications/notification.js';

function setup() {
  const handlers = new Map<string, (env: IncomingEnvelope) => Promise<void>>();
  const register = (eventType: string, handler: (env: IncomingEnvelope) => Promise<void>) => {
    handlers.set(eventType, handler);
  };
  const emitter: RealtimeEmitter = {
    emitToUser: vi.fn(),
    emitToConversation: vi.fn(),
    emitToCall: vi.fn(),
    emitToMeeting: vi.fn(),
    emitToTenant: vi.fn(),
    emitToTenantStaff: vi.fn(),
    emitToTenantMembers: vi.fn(),
  } as unknown as RealtimeEmitter;
  const supportTickets: SupportTicketRepository = {
    save: vi.fn(),
    findById: vi.fn(),
    listForCustomer: vi.fn(),
    countForCustomer: vi.fn(),
    listForAgentTenant: vi.fn(),
    countForAgentTenant: vi.fn(),
  };
  const analytics: AnalyticsRepository = {
    incrementCounters: vi.fn().mockResolvedValue(undefined),
    listForRange: vi.fn().mockResolvedValue([]),
  };
  const notifications: NotificationRepository = {
    createIfMissing: vi.fn().mockResolvedValue(true),
    findById: vi.fn(),
    update: vi.fn(),
    listForUser: vi.fn().mockResolvedValue([]),
    countUnread: vi.fn().mockResolvedValue(1),
  };
  const userPermissions: UserPermissionsProjectionRepository = {
    upsert: vi.fn(),
    upsertIdentityPreservingPermissions: vi.fn(),
    findByUserId: vi.fn(),
    findActiveByTenantAndPermission: vi.fn().mockResolvedValue([agentPermissions('agent-1')]),
    findActiveSupportRecipients: vi.fn().mockResolvedValue([agentPermissions('agent-1')]),
    markInactive: vi.fn(),
    markActive: vi.fn(),
    findActiveByTenantAndRoleId: vi.fn().mockResolvedValue([]),
  };

  bindSupportConsumers(register, { emitter, supportTickets, analytics, notifications, userPermissions });
  return { handlers, emitter, supportTickets, analytics, notifications, userPermissions };
}

function envelope(eventType: string, payload: Record<string, unknown>): IncomingEnvelope {
  return {
    eventId: 'evt-1',
    eventType,
    tenantId: 'tenant-office',
    correlationId: 'corr-1',
    occurredOnUtc: '2026-10-02T04:00:00.000Z',
    payload,
  };
}

function supportTicket() {
  const result = SupportTicket.open({
    tenantId: 'tenant-office',
    agentTenantId: 'tenant-platform',
    openedByUserId: 'user-office',
    conversationId: 'conversation-1',
    subject: 'App Mobile',
    category: 'Technical',
    priority: 'Normal',
    now: new Date('2026-10-02T04:00:00.000Z'),
  });
  if (!result.isSuccess) throw new Error(result.error.message);
  return result.value;
}

function agentPermissions(userId: string): UserPermissionsProjectionSnapshot {
  return {
    userId,
    tenantId: 'tenant-platform',
    permissions: ['communication.support.agent'],
    permissionVersion: 1,
    roleIds: [],
    actorType: 'PlatformAdmin',
    isActive: true,
    updatedAtUtc: new Date('2026-10-02T04:00:00.000Z'),
  };
}

describe('bindSupportConsumers', () => {
  it('traduce ticket opened a socket event para staff del tenant plataforma y oficina', async () => {
    const { handlers, emitter, analytics, notifications, userPermissions } = setup();

    await handlers.get('communication.support.opened.v1')!(
      envelope('communication.support.opened.v1', {
        TicketId: 'ticket-1',
        AgentTenantId: 'tenant-platform',
        SupportRecipientUserIds: ['agent-1'],
        OpenedByUserId: 'user-office',
        ConversationId: 'conversation-1',
        Subject: 'App Mobile',
        Category: 'Technical',
        Priority: 'Normal',
      }),
    );

    expect(emitter.emitToTenantStaff).toHaveBeenCalledTimes(2);
    const platformCall = vi.mocked(emitter.emitToTenantStaff).mock.calls[0]![0]!;
    expect(platformCall.tenantId).toBe('tenant-platform');
    expect(platformCall.event).toBe(SupportSocketEvents.TicketOpened);
    expect(platformCall.envelope.payload).toEqual(
      expect.objectContaining({
        ticketId: 'ticket-1',
        tenantId: 'tenant-office',
        agentTenantId: 'tenant-platform',
        changeType: 'opened',
      }),
    );
    expect(analytics.incrementCounters).toHaveBeenCalledWith({
      tenantId: 'tenant-office',
      day: '2026-10-02',
      increments: {
        supportTicketsOpened: 1,
      },
    });
    expect(userPermissions.findActiveSupportRecipients).not.toHaveBeenCalled();
    expect(notifications.createIfMissing).toHaveBeenCalledTimes(1);
    const created = vi.mocked(notifications.createIfMissing).mock.calls[0]![0] as Notification;
    const snapshot = created.toSnapshot() as NotificationSnapshot;
    expect(snapshot.tenantId).toBe('tenant-platform');
    expect(snapshot.userId).toBe('agent-1');
    expect(snapshot.kind).toBe('support.ticket.opened');
    expect(emitter.emitToUser).toHaveBeenCalledWith(
      expect.objectContaining({
        tenantId: 'tenant-platform',
        userId: 'agent-1',
        event: 'notification.received',
      }),
    );
  });

  it('usa fallback de proyeccion para eventos opened legacy sin destinatarios explicitos', async () => {
    const { handlers, notifications, userPermissions } = setup();

    await handlers.get('communication.support.opened.v1')!(
      envelope('communication.support.opened.v1', {
        TicketId: 'ticket-1',
        AgentTenantId: 'tenant-platform',
        OpenedByUserId: 'user-office',
        ConversationId: 'conversation-1',
        Subject: 'App Mobile',
        Category: 'Technical',
        Priority: 'Normal',
      }),
    );

    expect(userPermissions.findActiveSupportRecipients).toHaveBeenCalledWith(
      'tenant-platform',
      'communication.support.agent',
    );
    expect(notifications.createIfMissing).toHaveBeenCalledTimes(1);
  });

  it('emite changed y el evento especifico cuando un ticket cambia', async () => {
    const { handlers, emitter, supportTickets } = setup();
    vi.mocked(supportTickets.findById).mockResolvedValue(supportTicket());

    await handlers.get('communication.support.claimed.v1')!(
      envelope('communication.support.claimed.v1', {
        TicketId: 'ticket-1',
        AssignedAgentId: 'agent-1',
      }),
    );

    const events = vi.mocked(emitter.emitToTenantStaff).mock.calls.map((call) => call[0]!.event);
    expect(events).toContain(SupportSocketEvents.TicketClaimed);
    expect(events).toContain(SupportSocketEvents.TicketChanged);
    expect(emitter.emitToTenant).not.toHaveBeenCalled();
  });

  it('actualiza analytics cuando un ticket se resuelve', async () => {
    const { handlers, analytics, supportTickets } = setup();
    vi.mocked(supportTickets.findById).mockResolvedValue(supportTicket());

    await handlers.get('communication.support.resolved.v1')!(
      envelope('communication.support.resolved.v1', {
        TicketId: 'ticket-1',
        ResolvedByUserId: 'agent-1',
      }),
    );

    expect(analytics.incrementCounters).toHaveBeenCalledWith({
      tenantId: 'tenant-office',
      day: '2026-10-02',
      increments: {
        supportTicketsResolved: 1,
      },
    });
  });
});
