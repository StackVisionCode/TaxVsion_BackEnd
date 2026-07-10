import type { AnalyticsRepository } from '../ports/analytics-repository.js';
import type { IncomingEnvelope } from '../ports/event-consumer.js';

/**
 * Consumers propios que alimentan el snapshot diario. Cierra CRIT legacy: los
 * reportes NUNCA se calculan escaneando tablas operacionales (Conversation,
 * Message, Call, Meeting) — aqui se acumulan contadores por dia.
 */
export function bindAnalyticsConsumers(
  register: (eventType: string, handler: (env: IncomingEnvelope) => Promise<void>) => void,
  deps: { analytics: AnalyticsRepository },
): void {
  register('communication.chat.conversation_started.v1', async (env) => {
    await deps.analytics.incrementCounters({
      tenantId: env.tenantId,
      day: dayOf(env.occurredOnUtc),
      increments: { conversationsStarted: 1 },
    });
  });

  register('communication.chat.message_sent.v1', async (env) => {
    await deps.analytics.incrementCounters({
      tenantId: env.tenantId,
      day: dayOf(env.occurredOnUtc),
      increments: { messagesSent: 1 },
    });
  });

  register('communication.call.started.v1', async (env) => {
    await deps.analytics.incrementCounters({
      tenantId: env.tenantId,
      day: dayOf(env.occurredOnUtc),
      increments: { callsStarted: 1 },
    });
  });

  register('communication.call.ended.v1', async (env) => {
    const duration = getNumber(env.payload, 'durationSeconds', 'DurationSeconds') ?? 0;
    await deps.analytics.incrementCounters({
      tenantId: env.tenantId,
      day: dayOf(env.occurredOnUtc),
      increments: {
        callsEnded: 1,
        callMinutes: Math.floor(duration / 60),
      },
    });
  });

  register('communication.call.missed.v1', async (env) => {
    await deps.analytics.incrementCounters({
      tenantId: env.tenantId,
      day: dayOf(env.occurredOnUtc),
      increments: { missedCalls: 1 },
    });
  });

  register('communication.meeting.scheduled.v1', async (env) => {
    await deps.analytics.incrementCounters({
      tenantId: env.tenantId,
      day: dayOf(env.occurredOnUtc),
      increments: { meetingsScheduled: 1 },
    });
  });

  register('communication.meeting.started.v1', async (env) => {
    await deps.analytics.incrementCounters({
      tenantId: env.tenantId,
      day: dayOf(env.occurredOnUtc),
      increments: { meetingsStarted: 1 },
    });
  });

  register('communication.meeting.ended.v1', async (env) => {
    const duration = getNumber(env.payload, 'durationSeconds', 'DurationSeconds') ?? 0;
    await deps.analytics.incrementCounters({
      tenantId: env.tenantId,
      day: dayOf(env.occurredOnUtc),
      increments: { meetingsEnded: 1, meetingMinutes: Math.floor(duration / 60) },
    });
  });

  register('communication.support.opened.v1', async (env) => {
    await deps.analytics.incrementCounters({
      tenantId: env.tenantId,
      day: dayOf(env.occurredOnUtc),
      increments: { supportTicketsOpened: 1 },
    });
  });

  register('communication.support.resolved.v1', async (env) => {
    await deps.analytics.incrementCounters({
      tenantId: env.tenantId,
      day: dayOf(env.occurredOnUtc),
      increments: { supportTicketsResolved: 1 },
    });
  });
}

function dayOf(iso: string): string {
  return iso.slice(0, 10);
}

function getNumber(src: Record<string, unknown>, ...keys: string[]): number | undefined {
  for (const k of keys) {
    const v = src[k];
    if (typeof v === 'number') return v;
    if (typeof v === 'string') {
      const p = Number.parseInt(v, 10);
      if (Number.isFinite(p)) return p;
    }
  }
  return undefined;
}
