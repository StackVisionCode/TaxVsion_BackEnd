import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import type { Meeting } from '../../src/domain/meetings/meeting.js';
import type { IntegrationEvent } from '../../src/contracts/events/integration-event.js';
import type { IncomingEnvelope } from '../../src/application/ports/event-consumer.js';
import type { AppointmentRoomLock } from '../../src/application/ports/appointment-room-lock.js';
import { bindCalendarConsumers } from '../../src/application/event-handlers/calendar-consumers.js';

// B2 (Calendar): una cita tiene una sola sala aunque el aviso original y la reparación lleguen juntos.
function setup() {
  const meetings = new Map<string, Meeting>();
  const repo = {
    async findById(tenantId: string, id: string) {
      const m = meetings.get(id);
      return m && m.tenantId === tenantId ? m : null;
    },
    async findByAppointmentId(tenantId: string, appointmentId: string) {
      return [...meetings.values()].find((m) => m.tenantId === tenantId && m.toSnapshot().appointmentId === appointmentId) ?? null;
    },
    async save(m: Meeting) {
      // La escritura tarda: sin candado, los dos avisos verían que no hay sala.
      await new Promise((resolve) => setTimeout(resolve, 5));
      meetings.set(m.id, m);
    },
  };
  // Candado en memoria: serializa por cita, como sp_getapplock en SQL.
  const tails = new Map<string, Promise<unknown>>();
  const lock: AppointmentRoomLock = {
    async run(tenantId, appointmentId, work) {
      const key = `${tenantId}:${appointmentId}`;
      const previous = tails.get(key) ?? Promise.resolve();
      const current = previous.then(work, work);
      tails.set(key, current.catch(() => undefined));
      return current;
    },
  };
  const events: IntegrationEvent[] = [];
  const handlers = new Map<string, (env: IncomingEnvelope) => Promise<void>>();
  bindCalendarConsumers((type, handler) => handlers.set(type, handler), {
    meetings: repo,
    appointmentRoomLock: lock,
    publisher: { enqueue: async (e: IntegrationEvent) => void events.push(e) },
    passcodes: { hash: async () => 'h' },
    settings: { get: async () => ({ chatEnabled: true }) },
    meetingMediaPolicy: { get: async () => ({ meshMaxParticipants: 4, defaultMaxParticipants: 10 }) },
    effectiveSettings: {
      resolve: async () => ({ meeting: { defaultDurationMinutes: 60, maxDurationMinutes: 240, guestsAllowed: true } }),
    },
  } as never);
  return { handlers, meetings, events };
}

function envelope(eventType: string, tenantId: string, appointmentId: string): IncomingEnvelope {
  return {
    eventId: randomUUID(),
    eventType,
    tenantId,
    occurredOnUtc: new Date().toISOString(),
    payload: {
      TenantId: tenantId,
      AppointmentId: appointmentId,
      Title: 'Return review',
      OrganizerUserId: randomUUID(),
      StartUtc: new Date(Date.now() + 86_400_000).toISOString(),
      IsVirtual: true,
      CorrelationId: 'corr-1',
    },
  };
}

const linked = (events: IntegrationEvent[]) =>
  events.filter((e) => e.eventType === 'communication.meeting.linked_to_appointment.v1') as unknown as Array<{
    meetingId: string;
    appointmentId: string;
  }>;

describe('calendar consumers — una sala por cita', () => {
  it('el aviso original y la reparación a la vez crean una sola sala y ligan la misma', async () => {
    const { handlers, meetings, events } = setup();
    const tenantId = randomUUID();
    const appointmentId = randomUUID();

    await Promise.all([
      handlers.get('calendar.appointment_scheduled.v1')!(envelope('calendar.appointment_scheduled.v1', tenantId, appointmentId)),
      handlers.get('calendar.appointment_meeting_room_requested.v1')!(
        envelope('calendar.appointment_meeting_room_requested.v1', tenantId, appointmentId),
      ),
    ]);

    expect(meetings.size).toBe(1);
    const [room] = [...meetings.values()];
    expect(room!.toSnapshot().appointmentId).toBe(appointmentId);
    expect(linked(events).map((e) => e.meetingId)).toEqual([room!.id, room!.id]);
  });

  it('una reparación tardía no crea otra sala: vuelve a ligar la que existe', async () => {
    const { handlers, meetings, events } = setup();
    const tenantId = randomUUID();
    const appointmentId = randomUUID();

    await handlers.get('calendar.appointment_scheduled.v1')!(envelope('calendar.appointment_scheduled.v1', tenantId, appointmentId));
    await handlers.get('calendar.appointment_meeting_room_requested.v1')!(
      envelope('calendar.appointment_meeting_room_requested.v1', tenantId, appointmentId),
    );

    expect(meetings.size).toBe(1);
    expect(linked(events)).toHaveLength(2);
  });
});
