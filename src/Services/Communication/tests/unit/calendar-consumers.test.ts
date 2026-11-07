import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { Meeting } from '../../src/domain/meetings/meeting.js';
import type { MeetingInvitation } from '../../src/domain/meetings/meeting-invitation.js';
import type { MeetingRepository } from '../../src/application/ports/meeting-repository.js';
import type { IntegrationEvent } from '../../src/contracts/events/integration-event.js';
import type { IncomingEnvelope } from '../../src/application/ports/event-consumer.js';
import type { RealtimeEmitter } from '../../src/application/ports/realtime-emitter.js';
import { bindCalendarConsumers } from '../../src/application/event-handlers/calendar-consumers.js';

/** Cancelar la cita virtual cancela su sala; una ocurrencia suelta de la serie no. */
function setup() {
  const meetings = new Map<string, Meeting>();
  const repo = {
    async findById(tenantId: string, id: string) {
      const m = meetings.get(id);
      return m && m.tenantId === tenantId ? m : null;
    },
    async save(m: Meeting) {
      meetings.set(m.id, m);
    },
    async listInvitationsByMeeting(): Promise<MeetingInvitation[]> {
      return [];
    },
    async saveInvitation() {},
  } as unknown as MeetingRepository;
  const events: IntegrationEvent[] = [];
  const socket: string[] = [];
  const emitter = {
    emitToMeeting: (input: { event: string }) => socket.push(input.event),
    emitToUser: () => undefined,
  } as unknown as RealtimeEmitter;
  const handlers = new Map<string, (env: IncomingEnvelope) => Promise<void>>();
  const handle = (env: IncomingEnvelope) => {
    const handler = handlers.get('calendar.appointment_cancelled.v1');
    if (!handler) throw new Error('calendar.appointment_cancelled.v1 sin handler');
    return handler(env);
  };
  bindCalendarConsumers((type, handler) => handlers.set(type, handler), {
    meetings: repo,
    publisher: { enqueue: async (e: IntegrationEvent) => void events.push(e) },
    emitter,
  } as never);

  const host = randomUUID();
  const tenantId = randomUUID();
  const scheduled = Meeting.schedule({
    tenantId,
    title: 'Consulta',
    host: { userId: host, displayName: 'Host' },
    scheduledForUtc: new Date(Date.now() + 86_400_000),
  });
  if (!scheduled.isSuccess) throw new Error('schedule');
  const meeting = scheduled.value;
  meetings.set(meeting.id, meeting);
  return { handle, meeting, host, tenantId, events, socket };
}

function cancelled(tenantId: string, payload: Record<string, unknown>): IncomingEnvelope {
  return {
    eventId: randomUUID(),
    eventType: 'calendar.appointment_cancelled.v1',
    tenantId,
    occurredOnUtc: new Date().toISOString(),
    payload: { tenantId, appointmentId: randomUUID(), correlationId: 'corr-1', ...payload },
  };
}

const cancelledEvents = (events: IntegrationEvent[]) =>
  events.filter((e) => e.eventType === 'communication.meeting.cancelled.v1');

describe('bindCalendarConsumers — calendar.appointment_cancelled.v1', () => {
  it('cancela la sala de la cita y avisa como una cancelación normal', async () => {
    const ctx = setup();
    await ctx.handle(
      cancelled(ctx.tenantId, { scope: 'EntireSeries', meetingId: ctx.meeting.id, reason: 'no-show' }),
    );

    expect(ctx.meeting.toSnapshot().status).toBe('Cancelled');
    const [event] = cancelledEvents(ctx.events) as unknown as Array<{
      reason: string | null;
      cancelledByUserId: string;
    }>;
    expect(event?.reason).toBe('no-show');
    expect(event?.cancelledByUserId).toBe(ctx.host);
    expect(ctx.socket).toContain('meeting.cancelled');
  });

  it('acepta el payload en PascalCase y el alcance ThisAndFollowing (la cita entera queda cancelada)', async () => {
    const ctx = setup();

    await ctx.handle(cancelled(ctx.tenantId, { Scope: 'ThisAndFollowing', MeetingId: ctx.meeting.id }));

    expect(ctx.meeting.toSnapshot().status).toBe('Cancelled');
  });

  it('una ocurrencia suelta de la serie no cancela la sala de la serie', async () => {
    const ctx = setup();

    await ctx.handle(cancelled(ctx.tenantId, { scope: 'ThisOccurrence', meetingId: ctx.meeting.id }));

    expect(ctx.meeting.toSnapshot().status).toBe('Scheduled');
    expect(cancelledEvents(ctx.events)).toHaveLength(0);
  });

  it('es idempotente: un segundo aviso no vuelve a cancelar ni a avisar', async () => {
    const ctx = setup();
    await ctx.handle(cancelled(ctx.tenantId, { scope: 'EntireSeries', meetingId: ctx.meeting.id }));
    await ctx.handle(cancelled(ctx.tenantId, { scope: 'EntireSeries', meetingId: ctx.meeting.id }));

    expect(cancelledEvents(ctx.events)).toHaveLength(1);
  });

  it('no corta una sala que ya está en vivo', async () => {
    const ctx = setup();
    expect(ctx.meeting.start({ hostUserId: ctx.host }).isSuccess).toBe(true);

    await ctx.handle(cancelled(ctx.tenantId, { scope: 'EntireSeries', meetingId: ctx.meeting.id }));

    expect(ctx.meeting.toSnapshot().status).toBe('Live');
    expect(cancelledEvents(ctx.events)).toHaveLength(0);
  });

  it('sin sala ligada (cita no virtual o evento viejo) no hace nada', async () => {
    const ctx = setup();

    await ctx.handle(cancelled(ctx.tenantId, { scope: 'EntireSeries' }));

    expect(ctx.meeting.toSnapshot().status).toBe('Scheduled');
    expect(ctx.events).toHaveLength(0);
  });
});
