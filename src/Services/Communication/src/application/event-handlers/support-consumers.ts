import { randomUUID } from 'node:crypto';
import { setConversationArchived, type SetConversationArchivedDeps } from '../use-cases/set-conversation-archived.js';
import { computeSupportUniquenessKey } from '../../domain/conversations/uniqueness-key.js';
import type { IncomingEnvelope } from '../ports/event-consumer.js';
import type { NotificationRepository } from '../ports/notification-repository.js';
import type { RealtimeEmitter } from '../ports/realtime-emitter.js';
import type { SupportTicketRepository } from '../ports/support-ticket-repository.js';
import type { UserPermissionsProjectionRepository } from '../ports/user-permissions-projection-repository.js';
import { pushAndEmitNotification } from '../use-cases/push-notification.js';
import { SupportEventTypes } from '../../contracts/events/support-events.js';
import {
  SupportSocketEvents,
  type SupportTicketRealtimeDto,
} from '../../contracts/socket/support-socket-events.js';
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
    inAppSource: 'Communication' | 'Notification';
    /** F8a (D-13): cerrar archiva el chat del ticket y reabrir lo reactiva. */
    conversations?: SetConversationArchivedDeps['conversations'];
  },
): void {
  register(SupportEventTypes.Opened, async (env) => {
    const payload = buildOpenedPayload(env);
    if (!payload) return;

    // Realtime primero.
    emitToSupportRooms(env, deps.emitter, payload, SupportSocketEvents.TicketOpened);
    if (deps.inAppSource === 'Communication') await notifySupportQueue(env, deps, payload);

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
    await archiveTicketConversation(env, deps, true);
    await emitTicketChanged(
      env,
      deps,
      'closed',
      SupportSocketEvents.TicketClosed,
      getString(env.payload, 'closedByUserId', 'ClosedByUserId'),
    );
  });

  register(SupportEventTypes.Reopened, async (env) => {
    await archiveTicketConversation(env, deps, false);
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
  // El `env` que viene del evento `support.ticket.opened` tiene `tenantId` =
  // tenant del cliente que abrió el ticket (el pisante). El dispatcher a
  // notifications usa payload.agentTenantId (soporte). Construimos un envelope
  // con el agentTenantId para que la fila/socket caiga en el tenant correcto.
  const agentEnv: IncomingEnvelope = { ...env, tenantId: payload.agentTenantId };
  const occurredAtUtc = parseIso(env.occurredOnUtc);

  for (const userId of recipientUserIds) {
    await pushAndEmitNotification(
      agentEnv,
      {
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
        category: 'Work',
        // "Actor" útil: el usuario del tenant pisante que abrió el ticket (si
        // viene). Para support el agentes verán quién lo abrió en el otro lado.
        actorUserId: payload.changedByUserId ?? null,
        // Un ticket abierto por un cliente externo al tenant pisante no es ni
        // Employee ni Admin de este tenant — lo marcamos External para que el
        // front sepa que no debe buscar un avatar interno.
        actorType: 'External',
        resourceType: 'support_ticket',
        resourceId: payload.ticketId,
        ...(payload.conversationId ? { resourceContext: { conversationId: payload.conversationId } } : {}),
        action: 'review',
        occurredAtUtc,
      },
      deps,
    );
  }
}

function parseIso(value: string | null | undefined): Date {
  if (!value) return new Date();
  const parsed = new Date(value);
  return Number.isFinite(parsed.getTime()) ? parsed : new Date();
}

/** F8a (D-13): el chat del ticket sigue al estado del ticket (cerrado = solo lectura). */
async function archiveTicketConversation(
  env: IncomingEnvelope,
  deps: { emitter: RealtimeEmitter; supportTickets: SupportTicketRepository; conversations?: SetConversationArchivedDeps['conversations'] },
  archived: boolean,
): Promise<void> {
  if (!deps.conversations) return;
  const ticketId = getString(env.payload, 'ticketId', 'TicketId');
  if (!ticketId) return;
  const ticket = await deps.supportTickets.findById(ticketId);
  const snap = ticket?.toSnapshot();
  await setConversationArchived(
    {
      tenantId: snap?.tenantId ?? env.tenantId,
      archived,
      conversationId: snap?.conversationId ?? null,
      uniquenessKey: computeSupportUniquenessKey(ticketId),
    },
    { conversations: deps.conversations, emitter: deps.emitter },
  );
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
