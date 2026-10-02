import { randomUUID } from 'node:crypto';
import type { IncomingEnvelope } from '../ports/event-consumer.js';
import type { NotificationRepository } from '../ports/notification-repository.js';
import type { RealtimeEmitter } from '../ports/realtime-emitter.js';
import type { SupportTicketRepository } from '../ports/support-ticket-repository.js';
import type { UserPermissionsProjectionRepository } from '../ports/user-permissions-projection-repository.js';
import { pushNotification } from '../use-cases/push-notification.js';
import { SupportEventTypes } from '../../contracts/events/support-events.js';
import {
  SupportSocketEvents,
  type SupportTicketRealtimeDto,
} from '../../contracts/socket/support-socket-events.js';
import { NotificationSocketEvents } from '../../contracts/socket/notification-socket-events.js';
import { CommunicationPermissions } from '../../domain/shared/permissions.js';
import type { AnalyticsRepository } from '../ports/analytics-repository.js';

export function bindSupportConsumers(
  register: (eventType: string, handler: (env: IncomingEnvelope) => Promise<void>) => void,
  deps: {
    emitter: RealtimeEmitter;
    supportTickets: SupportTicketRepository;
    analytics: AnalyticsRepository;
    notifications: NotificationRepository;
    userPermissions: UserPermissionsProjectionRepository;
  },
): void {
  register(SupportEventTypes.Opened, async (env) => {
    const payload = buildOpenedPayload(env);
    if (!payload) return;

    // Realtime primero.
    emitToSupportRooms(env, deps.emitter, payload, SupportSocketEvents.TicketOpened);
    await notifySupportQueue(env, deps, payload);

    // Analytics projection from the same lifecycle event.
    await deps.analytics.incrementCounters({
      tenantId: env.tenantId,
      day: dayOf(env.occurredOnUtc),
      increments: {
        supportTicketsOpened: 1,
      },
    });
  });

  register(SupportEventTypes.Claimed, async (env) => {
    await emitTicketChanged(
      env,
      deps,
      'claimed',
      SupportSocketEvents.TicketClaimed,
      getString(env.payload, 'assignedAgentId', 'AssignedAgentId'),
    );
  });

  register(SupportEventTypes.Reassigned, async (env) => {
    await emitTicketChanged(
      env,
      deps,
      'reassigned',
      SupportSocketEvents.TicketReassigned,
      getString(env.payload, 'reassignedByUserId', 'ReassignedByUserId'),
    );
  });

  register(SupportEventTypes.Escalated, async (env) => {
    await emitTicketChanged(
      env,
      deps,
      'escalated',
      SupportSocketEvents.TicketEscalated,
      getString(env.payload, 'escalatedByUserId', 'EscalatedByUserId'),
    );
  });

  register(SupportEventTypes.Resolved, async (env) => {
    await emitTicketChanged(
      env,
      deps,
      'resolved',
      SupportSocketEvents.TicketResolved,
      getString(env.payload, 'resolvedByUserId', 'ResolvedByUserId'),
    );

    await deps.analytics.incrementCounters({
      tenantId: env.tenantId,
      day: dayOf(env.occurredOnUtc),
      increments: {
        supportTicketsResolved: 1,
      },
    });
  });

  register(SupportEventTypes.Closed, async (env) => {
    await emitTicketChanged(
      env,
      deps,
      'closed',
      SupportSocketEvents.TicketClosed,
      getString(env.payload, 'closedByUserId', 'ClosedByUserId'),
    );
  });

  register(SupportEventTypes.Reopened, async (env) => {
    await emitTicketChanged(
      env,
      deps,
      'reopened',
      SupportSocketEvents.TicketReopened,
      getString(env.payload, 'reopenedByUserId', 'ReopenedByUserId'),
    );
  });
}

async function notifySupportQueue(
  env: IncomingEnvelope,
  deps: {
    emitter: RealtimeEmitter;
    notifications: NotificationRepository;
    userPermissions: UserPermissionsProjectionRepository;
  },
  payload: SupportTicketRealtimeDto,
): Promise<void> {
  const recipientUserIds =
    payload.supportRecipientUserIds && payload.supportRecipientUserIds.length > 0
      ? payload.supportRecipientUserIds
      : (
          await deps.userPermissions.findActiveSupportRecipients(
            payload.agentTenantId,
            CommunicationPermissions.SupportAgent,
          )
        ).map((recipient) => recipient.userId);
  if (recipientUserIds.length === 0) return;

  const title = `New support ticket: ${payload.subject ?? 'Support request'}`;
  const body = `${payload.priority ?? 'Normal'} priority - ${payload.category ?? 'Other'}.`;

  for (const userId of recipientUserIds) {
    const result = await pushNotification(
      {
        tenantId: payload.agentTenantId,
        userId,
        kind: 'support.ticket.opened',
        priority: payload.priority === 'Urgent' || payload.priority === 'High' ? 'High' : 'Normal',
        title,
        body,
        metadata: {
          ticketId: payload.ticketId,
          tenantId: payload.tenantId,
          conversationId: payload.conversationId,
        },
        sourceEventId: env.eventId,
        sourceEventType: env.eventType,
        correlationId: env.correlationId ?? null,
      },
      deps,
    );

    if (!result.isSuccess || !result.value.created || !result.value.notification) continue;

    deps.emitter.emitToUser({
      tenantId: payload.agentTenantId,
      userId,
      event: NotificationSocketEvents.Received,
      envelope: {
        eventId: randomUUID(),
        correlationId: env.correlationId ?? '',
        emittedAtUtc: new Date().toISOString(),
        payload: result.value.notification,
      },
    });
    deps.emitter.emitToUser({
      tenantId: payload.agentTenantId,
      userId,
      event: NotificationSocketEvents.UnreadCountChanged,
      envelope: {
        eventId: randomUUID(),
        correlationId: env.correlationId ?? '',
        emittedAtUtc: new Date().toISOString(),
        payload: { count: result.value.unreadCount },
      },
    });
  }
}

async function emitTicketChanged(
  env: IncomingEnvelope,
  deps: { emitter: RealtimeEmitter; supportTickets: SupportTicketRepository },
  changeType: SupportTicketRealtimeDto['changeType'],
  event: string,
  changedByUserId: string | undefined,
): Promise<void> {
  const ticketId = getString(env.payload, 'ticketId', 'TicketId');
  if (!ticketId) return;

  const ticket = await deps.supportTickets.findById(ticketId);
  if (!ticket) return;
  const snap = ticket.toSnapshot();
  const payload: SupportTicketRealtimeDto = {
    ticketId,
    tenantId: snap.tenantId,
    agentTenantId: snap.agentTenantId,
    conversationId: snap.conversationId,
    subject: snap.subject,
    category: snap.category,
    priority: snap.priority,
    status: snap.status,
    assignedAgentId: snap.assignedAgentId,
    ...(changedByUserId !== undefined ? { changedByUserId } : {}),
    changeType,
    occurredOnUtc: env.occurredOnUtc,
  };

  emitToSupportRooms(env, deps.emitter, payload, event);
  emitToSupportRooms(env, deps.emitter, payload, SupportSocketEvents.TicketChanged);
}

function buildOpenedPayload(env: IncomingEnvelope): SupportTicketRealtimeDto | null {
  const ticketId = getString(env.payload, 'ticketId', 'TicketId');
  const agentTenantId = getString(env.payload, 'agentTenantId', 'AgentTenantId');
  const openedByUserId = getString(env.payload, 'openedByUserId', 'OpenedByUserId');
  const conversationId = getString(env.payload, 'conversationId', 'ConversationId');
  const supportRecipientUserIds = getStringArray(
    env.payload,
    'supportRecipientUserIds',
    'SupportRecipientUserIds',
  );
  if (!ticketId || !agentTenantId || !conversationId) return null;

  return {
    ticketId,
    tenantId: env.tenantId,
    agentTenantId,
    ...(supportRecipientUserIds.length > 0 ? { supportRecipientUserIds } : {}),
    conversationId,
    ...optional('subject', getString(env.payload, 'subject', 'Subject')),
    ...optional('category', getString(env.payload, 'category', 'Category')),
    ...optional('priority', getString(env.payload, 'priority', 'Priority')),
    status: 'Open',
    assignedAgentId: null,
    ...(openedByUserId !== undefined ? { changedByUserId: openedByUserId } : {}),
    changeType: 'opened',
    occurredOnUtc: env.occurredOnUtc,
  };
}

function emitToSupportRooms(
  env: IncomingEnvelope,
  emitter: RealtimeEmitter,
  payload: SupportTicketRealtimeDto,
  event: string,
): void {
  const envelope = {
    eventId: randomUUID(),
    correlationId: env.correlationId ?? '',
    emittedAtUtc: new Date().toISOString(),
    payload,
  };

  emitter.emitToTenantStaff({ tenantId: payload.agentTenantId, event, envelope });
  emitter.emitToTenantStaff({ tenantId: payload.tenantId, event, envelope });
}

function getString(source: Record<string, unknown>, ...keys: string[]): string | undefined {
  for (const key of keys) {
    const value = source[key];
    if (typeof value === 'string') return value;
  }
  return undefined;
}

function getStringArray(source: Record<string, unknown>, ...keys: string[]): readonly string[] {
  for (const key of keys) {
    const value = source[key];
    if (Array.isArray(value)) {
      return value.filter((item: unknown): item is string => typeof item === 'string');
    }
  }
  return [];
}

function optional<TKey extends keyof SupportTicketRealtimeDto>(
  key: TKey,
  value: SupportTicketRealtimeDto[TKey] | undefined,
): Partial<SupportTicketRealtimeDto> {
  return value === undefined ? {} : { [key]: value };
}

function dayOf(iso: string): string {
  return iso.slice(0, 10);
}
